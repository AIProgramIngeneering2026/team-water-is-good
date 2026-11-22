namespace NVKDrive.Application.Bookings;

public sealed record BookingResult(
    Guid BookingId,
    Guid TripId,
    string Status,
    int SeatCount,
    int RemainingSeats);