namespace NVKDrive.Application.Trips;

public sealed record TripCompletionResult(
    Guid TripId,
    string Status,
    DateTimeOffset CompletedAt,
    int CompletedBookingsCount);