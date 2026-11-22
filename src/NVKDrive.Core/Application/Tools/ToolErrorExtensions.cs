using System.Diagnostics.CodeAnalysis;

namespace NVKDrive.Core;

internal static class ToolErrorExtensions
{
    internal static ToolResult<TResult>.Failure ToFailure<TResult>(this ToolError error) =>
        new(error);

    internal static ToolError InvalidParameter(string field, string message) =>
        new("INVALID_PARAMETER", message, field);

    internal static ToolError UnknownOperation(string operation) =>
        new("UNKNOWN_OPERATION", $"Operation '{operation}' is not registered.", "operation");

    internal static ToolError ExecutionFailed() =>
        new("TOOL_EXECUTION_FAILED", "Tool execution failed.");

    internal static ToolError ExpiredLease() =>
        new("JOB_LEASE_EXPIRED", "The job processing lease has expired.", "leaseToken");

    [return: NotNullIfNotNull(nameof(error))]
    internal static ToolError? ToToolError(this BookingJobError? error) =>
        error is null ? null : new ToolError(error.Code, error.Message, error.Field);

    internal static BookingJobError ToBookingJobError(this ToolError error) =>
        new(error.Code, error.Message, error.Field);
}
