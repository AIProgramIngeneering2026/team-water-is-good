namespace NVKDrive.Application.Bookings;

public sealed record BookingCommand(
    BookingOperation Operation,
    Guid TripId,
    Guid? BookingId,
    int? SeatCount,
    bool? BookingForOthers);