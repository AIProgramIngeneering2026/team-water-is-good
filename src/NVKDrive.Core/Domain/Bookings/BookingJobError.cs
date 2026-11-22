namespace NVKDrive.Core;

public sealed record BookingJobError(string Code, string Message, string? Field = null);
