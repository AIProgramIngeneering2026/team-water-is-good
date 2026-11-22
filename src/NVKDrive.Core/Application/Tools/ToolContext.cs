namespace NVKDrive.Application.Tools;

public sealed record ToolContext(
    Guid OperationId,
    Guid? UserId,
    Guid? JobId = null,
    string? LeaseToken = null);