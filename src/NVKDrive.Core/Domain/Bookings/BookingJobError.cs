namespace NVKDrive.Domain.Bookings;

public sealed record BookingJobError(string Code, string Message, string? Field = null);