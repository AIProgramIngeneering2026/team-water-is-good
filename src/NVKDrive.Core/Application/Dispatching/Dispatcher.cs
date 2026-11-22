using NVKDrive.Application.Storage;
using NVKDrive.Application.Tools;
using NVKDrive.Domain.Bookings;

namespace NVKDrive.Application.Dispatching;

public sealed class Dispatcher(
    ToolRegistry registry,
    ICoreStore store,
    TimeProvider timeProvider,
    TimeSpan leaseDuration)
{
    private readonly Guid workerId = Guid.NewGuid();
    private readonly TimeSpan leaseDuration = ValidateDuration(leaseDuration);

    public BookingJob? ReadJob(Guid jobId)
    {
        ExpireJob(jobId);
        return store.FindJob(jobId);
    }

    public Task<ToolResult<TResult>> DispatchAsync<TCommand, TResult>(
        string operation,
        Guid tripId,
        TCommand command,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        return DispatchCoreAsync<TCommand, TResult>(
            operation, tripId, null, command, userId, cancellationToken);
    }

    public Task<ToolResult<TResult>> DispatchBookingAsync<TCommand, TResult>(
        string operation,
        Guid tripId,
        Guid jobId,
        TCommand command,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        return DispatchCoreAsync<TCommand, TResult>(
            operation, tripId, jobId, command, userId, cancellationToken);
    }

    private async Task<ToolResult<TResult>> DispatchCoreAsync<TCommand, TResult>(
        string operation,
        Guid tripId,
        Guid? jobId,
        TCommand command,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = registry.Resolve<TCommand, TResult>(operation);
        if (registration is ToolResult<Func<IToolSession, ITool<TCommand, TResult>>>.Failure unknown)
            return new ToolResult<TResult>.Failure(unknown.Error);

        var factory = ((ToolResult<Func<IToolSession, ITool<TCommand, TResult>>>.Success)registration).Value;
        var leaseToken = jobId.HasValue ? Guid.NewGuid().ToString("N") : null;
        if (jobId is Guid id)
        {
            ExpireJob(id);
            var startError = store.UpdateJob(id, job =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = timeProvider.GetUtcNow();
                return job.Start(new JobLease(workerId, leaseToken!, now + leaseDuration), now).ToToolError();
            });
            if (startError is not null) return new ToolResult<TResult>.Failure(startError);
        }

        var context = new ToolContext(Guid.NewGuid(), userId, jobId, leaseToken);
        await using var session = await store.BeginAsync(tripId, cancellationToken);

        // A trip session can wait behind an earlier operation on the same trip.
        // Check the lease again before invoking Tool.
        cancellationToken.ThrowIfCancellationRequested();
        if (jobId is Guid claimedId && ReadJob(claimedId)?.Lease?.Token != leaseToken)
            return ToolErrorExtensions.ExpiredLease().ToFailure<TResult>();

        cancellationToken.ThrowIfCancellationRequested();
        var result = await ExecuteToolAsync(factory, session, command, context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ToolError? writeError;
        if (result is ToolResult<TResult>.Failure failure)
            writeError = jobId is Guid failedId
                ? store.UpdateJob(failedId, job =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return job.Fail(leaseToken!, timeProvider.GetUtcNow(), failure.Error.ToBookingJobError())
                        .ToToolError();
                })
                : null;
        else
            writeError = jobId is Guid confirmedId
                ? session.Commit(confirmedId, job =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return job.Confirm(leaseToken!, timeProvider.GetUtcNow()).ToToolError();
                })
                : session.Commit();

        if (writeError is null) return result;

        if (jobId is Guid pendingId && writeError.Code == "JOB_LEASE_EXPIRED") ExpireJob(pendingId);

        return new ToolResult<TResult>.Failure(writeError);
    }

    private static async Task<ToolResult<TResult>> ExecuteToolAsync<TCommand, TResult>(
        Func<IToolSession, ITool<TCommand, TResult>> factory,
        IToolSession session,
        TCommand command,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await factory(session).ExecuteAsync(command, context, cancellationToken);
            return result is ToolResult<TResult>.Success or ToolResult<TResult>.Failure
                ? result
                : ToolErrorExtensions.ExecutionFailed().ToFailure<TResult>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ToolErrorExtensions.ExecutionFailed().ToFailure<TResult>();
        }
    }

    private void ExpireJob(Guid jobId)
    {
        store.UpdateJob(jobId, job => job.ExpireLease(timeProvider.GetUtcNow()).ToToolError());
    }

    private static TimeSpan ValidateDuration(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return duration;
    }
}