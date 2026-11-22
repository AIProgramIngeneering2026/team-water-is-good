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
