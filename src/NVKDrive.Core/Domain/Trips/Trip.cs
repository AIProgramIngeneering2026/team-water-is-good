namespace NVKDrive.Domain.Trips;

public sealed record Trip(
    Guid Id,
    Guid DriverId,
    int RemainingSeats,
    DateTimeOffset? DepartureTime,
    DateTimeOffset? ArrivalTime,
    TripStatus Status = TripStatus.Active,
    DateTimeOffset? CompletedAt = null);