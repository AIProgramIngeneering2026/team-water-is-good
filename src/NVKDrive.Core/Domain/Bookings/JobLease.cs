namespace NVKDrive.Domain.Bookings;

public sealed record JobLease(Guid WorkerId, string Token, DateTimeOffset ExpiresAt);