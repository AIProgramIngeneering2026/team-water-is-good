namespace NVKDrive.Core;

public interface ITool<TCommand, TResult>
{
    Task<ToolResult<TResult>> ExecuteAsync(
        TCommand command,
        ToolContext context,
        CancellationToken cancellationToken);
}

public sealed record ToolContext(
    Guid OperationId, Guid? UserId, Guid? JobId = null, string? LeaseToken = null);

public abstract record ToolResult<TResult>
{
    public sealed record Success(TResult Value) : ToolResult<TResult>;
    public sealed record Failure(ToolError Error) : ToolResult<TResult>;
}

public sealed record ToolError(string Code, string Message, string? Field = null);

public enum BookingOperation { Create, CancelByPassenger, CancelByDriver }

public sealed record BookingCommand(
    BookingOperation Operation, Guid TripId, Guid? BookingId, int? SeatCount, bool? BookingForOthers);

public sealed record BookingResult(
    Guid BookingId, Guid TripId, string Status, int SeatCount, int RemainingSeats);

public enum CompletionReason { Manual, Automatic }

public sealed record TripCompletionCommand(Guid TripId, CompletionReason Reason);

public sealed record TripCompletionResult(
    Guid TripId, string Status, DateTimeOffset CompletedAt, int CompletedBookingsCount);
