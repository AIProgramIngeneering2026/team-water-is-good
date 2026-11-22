namespace NVKDrive.Application.Tools;

public sealed record ToolError(string Code, string Message, string? Field = null);