namespace NVKDrive.Core;

public sealed record Booking(
    Guid Id, Guid TripId, Guid JobId, Guid PassengerId, int SeatCount,
    BookingJobStatus Status = BookingJobStatus.Confirmed, bool BookingForOthers = false);
