namespace NVKDrive.Application.Trips;

public sealed record TripCompletionCommand(Guid TripId, CompletionReason Reason);