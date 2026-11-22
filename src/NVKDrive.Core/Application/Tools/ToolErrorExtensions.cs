using System.Diagnostics.CodeAnalysis;
using NVKDrive.Domain.Bookings;

namespace NVKDrive.Application.Tools;

internal static class ToolErrorExtensions
{
    internal static ToolResult<TResult>.Failure ToFailure<TResult>(this ToolError error)
    {
        return new ToolResult<TResult>.Failure(error);
    }

    internal static ToolError InvalidParameter(string field, string message)
    {
        return new ToolError("INVALID_PARAMETER", message, field);
    }

    internal static ToolError UnknownOperation(string operation)
    {
        return new ToolError("UNKNOWN_OPERATION", $"Operation '{operation}' is not registered.", "operation");
    }

    internal static ToolError ExecutionFailed()
    {
        return new ToolError("TOOL_EXECUTION_FAILED", "Tool execution failed.");
    }

    internal static ToolError ExpiredLease()
    {
        return new ToolError("JOB_LEASE_EXPIRED", "The job processing lease has expired.", "leaseToken");
    }

    [return: NotNullIfNotNull(nameof(error))]
    internal static ToolError? ToToolError(this BookingJobError? error)
    {
        return error is null ? null : new ToolError(error.Code, error.Message, error.Field);
    }

    internal static BookingJobError ToBookingJobError(this ToolError error)
    {
        return new BookingJobError(error.Code, error.Message, error.Field);
    }
}