using NVKDrive.Application.Bookings;
using NVKDrive.Application.Storage;
using NVKDrive.Application.Tools;
using NVKDrive.Application.Trips;
using NVKDrive.Domain.Bookings;
using NVKDrive.Domain.Trips;

namespace NVKDrive.Core.Tests.Application.Tools;

public sealed class ToolsTests
{
    [Theory]
    [InlineData(null, false, "INVALID_PARAMETER", "seatCount")]
    [InlineData(0, false, "INVALID_SEAT_COUNT", "seatCount")]
    [InlineData(1, null, "INVALID_PARAMETER", "bookingForOthers")]
    [InlineData(2, false, "BOOKING_FOR_OTHERS_REQUIRED", "bookingForOthers")]
    [InlineData(3, true, "INSUFFICIENT_SEATS", "seatCount")]
    public async Task BookingFailureIdentifiesFieldAndPreservesSeats(
        int? seatCount, bool? bookingForOthers, string expectedCode, string expectedField)
    {
        var trip = NewTrip();
        var session = new TestSession(trip);
        var command = new BookingCommand(BookingOperation.Create, trip.Id, null, seatCount, bookingForOthers);
        var context = new ToolContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "lease");

        var result = await new BookingTool(session).ExecuteAsync(command, context, CancellationToken.None);

        var failure = Assert.IsType<ToolResult<BookingResult>.Failure>(result);
        Assert.Equal(expectedCode, failure.Error.Code);
        Assert.Equal(expectedField, failure.Error.Field);
        Assert.Equal(trip, session.FindTrip(trip.Id));
        Assert.Empty(session.GetBookings(trip.Id));
    }

    [Fact]
    public async Task DriverCannotBookOwnTrip()
    {
        var trip = NewTrip();
        var session = new TestSession(trip);
        var command = new BookingCommand(BookingOperation.Create, trip.Id, null, 1, false);
        var context = new ToolContext(Guid.NewGuid(), trip.DriverId, Guid.NewGuid(), "lease");

        var result = await new BookingTool(session).ExecuteAsync(command, context, CancellationToken.None);

        var failure = Assert.IsType<ToolResult<BookingResult>.Failure>(result);
        Assert.Equal("SELF_BOOKING", failure.Error.Code);
        Assert.Equal(trip, session.FindTrip(trip.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AutomaticCompletionUsesArrivalWhenPresentAndAllowsExactThreshold(bool hasArrivalTime)
    {
        var trip = NewTrip();
        if (hasArrivalTime)
            trip = trip with { ArrivalTime = trip.DepartureTime!.Value.AddHours(1) };

        var session = new TestSession(trip);
        var threshold = (trip.ArrivalTime ?? trip.DepartureTime)!.Value.AddHours(2);
        var clock = new TestClock { Now = threshold.AddTicks(-1) };
        var tool = new TripCompletionTool(session, clock);
        var command = new TripCompletionCommand(trip.Id, CompletionReason.Automatic);
        var context = new ToolContext(Guid.NewGuid(), null);

        var earlyResult = await tool.ExecuteAsync(command, context, CancellationToken.None);

        var failure = Assert.IsType<ToolResult<TripCompletionResult>.Failure>(earlyResult);
        Assert.Equal("COMPLETION_TIME_NOT_REACHED", failure.Error.Code);
        Assert.Equal(TripStatus.Active, session.FindTrip(trip.Id)?.Status);

        clock.Now = threshold;
        var result = await tool.ExecuteAsync(command, context, CancellationToken.None);

        var success = Assert.IsType<ToolResult<TripCompletionResult>.Success>(result);
        Assert.Equal(threshold, success.Value.CompletedAt);
        Assert.Equal(TripStatus.Completed, session.FindTrip(trip.Id)?.Status);
    }

    [Fact]
    public async Task ManualCompletionRequiresTripDriver()
    {
        var trip = NewTrip();
        var session = new TestSession(trip);
        var command = new TripCompletionCommand(trip.Id, CompletionReason.Manual);
        var context = new ToolContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await new TripCompletionTool(session, TimeProvider.System)
            .ExecuteAsync(command, context, CancellationToken.None);

        var failure = Assert.IsType<ToolResult<TripCompletionResult>.Failure>(result);
        Assert.Equal("FORBIDDEN", failure.Error.Code);
        Assert.Equal(trip, session.FindTrip(trip.Id));
    }

    private static Trip NewTrip()
    {
        return new Trip(
            Guid.NewGuid(), Guid.NewGuid(), 2, new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero), null);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    private sealed class TestSession(Trip trip) : IToolSession
    {
        private readonly Dictionary<Guid, Booking> bookings = [];
        private Trip currentTrip = trip;

        public Trip? FindTrip(Guid tripId)
        {
            return currentTrip.Id == tripId ? currentTrip : null;
        }

        public Booking? FindBooking(Guid bookingId)
        {
            return bookings.GetValueOrDefault(bookingId);
        }

        public BookingJob? FindJob(Guid jobId)
        {
            throw new NotSupportedException();
        }

        public IEnumerable<Booking> GetBookings(Guid tripId)
        {
            return bookings.Values.Where(booking => booking.TripId == tripId);
        }

        public void Save(Trip updatedTrip)
        {
            currentTrip = updatedTrip;
        }

        public void Save(Booking booking)
        {
            bookings[booking.Id] = booking;
        }

        public void Save(BookingJob job)
        {
            throw new NotSupportedException();
        }

        public ToolError? Commit(Guid? jobId = null, Func<BookingJob, ToolError?>? update = null)
        {
            throw new NotSupportedException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}