namespace NVKDrive.Core;

public enum TripStatus { Active, Completed, Cancelled }

public sealed record Trip(
    Guid Id, Guid DriverId, int RemainingSeats,
    DateTimeOffset? DepartureTime, DateTimeOffset? ArrivalTime,
    TripStatus Status = TripStatus.Active, DateTimeOffset? CompletedAt = null);

public sealed record Booking(
    Guid Id, Guid TripId, Guid JobId, Guid PassengerId, int SeatCount,
    BookingJobStatus Status = BookingJobStatus.Confirmed, bool BookingForOthers = false);

public interface ICoreStore
{
    BookingJob? FindJob(Guid jobId);
    ToolError? UpdateJob(Guid jobId, Func<BookingJob, ToolError?> update);
    ValueTask<IToolTransaction> BeginAsync(Guid tripId, CancellationToken cancellationToken);
}

public interface IToolSession
{
    Trip? FindTrip(Guid tripId);
    Booking? FindBooking(Guid bookingId);
    BookingJob? FindJob(Guid jobId);
    IEnumerable<Booking> GetBookings(Guid tripId);
    void Save(Trip trip);
    void Save(Booking booking);
    void Save(BookingJob job);
}

public interface IToolTransaction : IToolSession, IAsyncDisposable
{
    ToolError? Commit(Guid? jobId = null, Func<BookingJob, ToolError?>? update = null);
}
