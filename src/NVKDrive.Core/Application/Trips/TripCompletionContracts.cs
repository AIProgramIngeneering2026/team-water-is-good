namespace NVKDrive.Core;

public enum CompletionReason { Manual, Automatic }

public sealed record TripCompletionCommand(Guid TripId, CompletionReason Reason);

public sealed record TripCompletionResult(
    Guid TripId, string Status, DateTimeOffset CompletedAt, int CompletedBookingsCount);
