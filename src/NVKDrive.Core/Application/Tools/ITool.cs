namespace NVKDrive.Application.Tools;

public interface ITool<TCommand, TResult>
{
    Task<ToolResult<TResult>> ExecuteAsync(
        TCommand command,
        ToolContext context,
        CancellationToken cancellationToken);
}