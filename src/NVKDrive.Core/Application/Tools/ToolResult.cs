namespace NVKDrive.Application.Tools;

public abstract record ToolResult<TResult>
{
    public sealed record Success(TResult Value) : ToolResult<TResult>;

    public sealed record Failure(ToolError Error) : ToolResult<TResult>;
}