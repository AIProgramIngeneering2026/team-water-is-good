namespace NVKDrive.Domain.Bookings;

public enum BookingJobStatus
{
    Created,
    Processing,
    Confirmed,
    Failed,
    Completed,
    CancelledByPassenger,
    CancelledByDriver,
    CancelledBecauseTripCancelled
}