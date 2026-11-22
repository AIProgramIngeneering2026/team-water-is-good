namespace NVKDrive.Core;

public enum BookingOperation { Create, CancelByPassenger, CancelByDriver }

public sealed record BookingCommand(
    BookingOperation Operation, Guid TripId, Guid? BookingId, int? SeatCount, bool? BookingForOthers);

public sealed record BookingResult(
    Guid BookingId, Guid TripId, string Status, int SeatCount, int RemainingSeats);
