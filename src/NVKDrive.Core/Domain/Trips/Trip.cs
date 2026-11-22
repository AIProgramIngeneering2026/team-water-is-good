namespace NVKDrive.Core;

public enum TripStatus { Active, Completed, Cancelled }

public sealed record Trip(
    Guid Id, Guid DriverId, int RemainingSeats,
    DateTimeOffset? DepartureTime, DateTimeOffset? ArrivalTime,
    TripStatus Status = TripStatus.Active, DateTimeOffset? CompletedAt = null);
