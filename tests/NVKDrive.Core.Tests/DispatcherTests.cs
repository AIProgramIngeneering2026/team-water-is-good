namespace NVKDrive.Core.Tests;

public sealed class DispatcherTests
{
    private const string Operation = "example";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private readonly TestClock clock = new();
    private readonly Guid jobId = Guid.NewGuid();
    private readonly Guid tripId = Guid.NewGuid();
    private readonly Guid passengerId = Guid.NewGuid();
    private readonly ToolRegistry registry = new();
    private readonly InMemoryCoreStore store;
    private readonly Dispatcher dispatcher;

    public DispatcherTests()
    {
        var trip = new Trip(tripId, Guid.NewGuid(), 2, null, null);
        store = new InMemoryCoreStore([trip], [new BookingJob(jobId)]);
        dispatcher = new Dispatcher(registry, store, clock, LeaseDuration);
    }

    [Fact]
    public async Task UnknownOperationDoesNotClaimJob()
    {
        var result = await DispatchBookingAsync();

        Assert.Equal("UNKNOWN_OPERATION", ErrorOf(result).Code);
        Assert.Equal(BookingJobStatus.Created, dispatcher.ReadJob(jobId)?.Status);
    }

    [Fact]
    public async Task NewlyRegisteredToolRunsWithoutClaimingJobOrRequiringUser()
    {
        registry.Register<string, string>(Operation, _ => new TestTool((command, context, _) =>
        {
            Assert.Null(context.JobId);
            Assert.Null(context.LeaseToken);
            Assert.Null(context.UserId);
            Assert.NotEqual(Guid.Empty, context.OperationId);
            return Task.FromResult<ToolResult<string>>(new ToolResult<string>.Success(command));
        }));

        var result = await dispatcher.DispatchAsync<string, string>(Operation, tripId, "command");

        Assert.Equal("command", Assert.IsType<ToolResult<string>.Success>(result).Value);
        Assert.Equal(BookingJobStatus.Created, dispatcher.ReadJob(jobId)?.Status);
    }

    [Fact]
    public async Task Z1_ConcurrentDispatchInvokesToolOnlyOnce()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        registry.Register<string, string>(Operation, _ => new TestTool(async (_, _, cancellationToken) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await finish.Task.WaitAsync(cancellationToken);
            return new ToolResult<string>.Success("done");
        }));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = DispatchBookingAsync(timeout.Token);
        try
        {
            await started.Task.WaitAsync(timeout.Token);
            Assert.Equal(BookingJobStatus.Processing, dispatcher.ReadJob(jobId)?.Status);
            var duplicate = await DispatchBookingAsync(timeout.Token);
            Assert.Equal("JOB_ALREADY_PROCESSING", ErrorOf(duplicate).Code);
        }
        finally
        {
            finish.TrySetResult();
        }

        Assert.IsType<ToolResult<string>.Success>(await first);
        Assert.Equal(1, calls);
        Assert.Equal(BookingJobStatus.Confirmed, dispatcher.ReadJob(jobId)?.Status);
    }

    [Fact]
    public async Task ToolExceptionFailsJobAndDiscardsStagedWrites()
    {
        var bookingId = Guid.NewGuid();
        RegisterStagingTool(bookingId, () => throw new InvalidOperationException("private details"));

        var result = await DispatchBookingAsync();

        var error = ErrorOf(result);
        Assert.Equal("TOOL_EXECUTION_FAILED", error.Code);
        Assert.DoesNotContain("private details", error.Message);
        Assert.Equal(BookingJobStatus.Failed, dispatcher.ReadJob(jobId)?.Status);
        Assert.Equal(error, dispatcher.ReadJob(jobId)?.Error);
        await AssertNoBookingAsync(bookingId);
    }

    [Fact]
    public async Task ExpiringLeaseRejectsSuccessfulToolResultAndDiscardsStagedWrites()
    {
        var bookingId = Guid.NewGuid();
        RegisterStagingTool(bookingId, () => clock.Advance(LeaseDuration));

        var result = await DispatchBookingAsync();

        Assert.Equal("JOB_LEASE_EXPIRED", ErrorOf(result).Code);
        Assert.Equal(BookingJobStatus.Failed, store.FindJob(jobId)?.Status);
        Assert.Equal("PROCESSING_INTERRUPTED", store.FindJob(jobId)?.Error?.Code);
        await AssertNoBookingAsync(bookingId);
    }

    [Fact]
    public async Task CancellingDispatchDiscardsStagedWritesAndLeavesJobForLeaseExpiry()
    {
        using var cancellation = new CancellationTokenSource();
        var bookingId = Guid.NewGuid();
        RegisterStagingTool(bookingId, cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DispatchBookingAsync(cancellation.Token));

        await AssertNoBookingAsync(bookingId);
        Assert.Equal(BookingJobStatus.Processing, dispatcher.ReadJob(jobId)?.Status);
        clock.Advance(LeaseDuration);
        Assert.Equal(BookingJobStatus.Failed, dispatcher.ReadJob(jobId)?.Status);
        Assert.Equal("PROCESSING_INTERRUPTED", dispatcher.ReadJob(jobId)?.Error?.Code);
    }

    private Task<ToolResult<string>> DispatchBookingAsync(CancellationToken cancellationToken = default)
    {
        return dispatcher.DispatchBookingAsync<string, string>(
            Operation, tripId, jobId, "command", passengerId, cancellationToken);
    }

    private void RegisterStagingTool(Guid bookingId, Action afterStage)
    {
        registry.Register<string, string>(Operation, session => new TestTool((_, _, _) =>
        {
            var trip = session.FindTrip(tripId)!;
            session.Save(trip with { RemainingSeats = trip.RemainingSeats - 1 });
            session.Save(new Booking(bookingId, tripId, jobId, passengerId, 1));
            afterStage();
            return Task.FromResult<ToolResult<string>>(new ToolResult<string>.Success("done"));
        }));
    }

    private async Task AssertNoBookingAsync(Guid bookingId)
    {
        await using var session = await store.BeginAsync(tripId, CancellationToken.None);
        Assert.Null(session.FindBooking(bookingId));
        Assert.Equal(2, session.FindTrip(tripId)?.RemainingSeats);
    }

    private static ToolError ErrorOf(ToolResult<string> result)
    {
        return Assert.IsType<ToolResult<string>.Failure>(result).Error;
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class TestTool(
        Func<string, ToolContext, CancellationToken, Task<ToolResult<string>>> execute) : ITool<string, string>
    {
        public Task<ToolResult<string>> ExecuteAsync(
            string command, ToolContext context, CancellationToken cancellationToken)
        {
            return execute(command, context, cancellationToken);
        }
    }
}
