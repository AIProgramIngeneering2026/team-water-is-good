namespace NVKDrive.Core;

public sealed class BookingTool(IToolSession session) : ITool<BookingCommand, BookingResult>
{
    public Task<ToolResult<BookingResult>> ExecuteAsync(
        BookingCommand command,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.IsDefined(command.Operation))
            return Task.FromResult(InvalidParameter("operation", "Unknown booking operation"));

        if (context.UserId is not Guid userId || userId == Guid.Empty)
            return Task.FromResult(InvalidParameter("userId", "A user identifier is required"));

        var result = command.Operation == BookingOperation.Create
            ? Create(command, context, userId)
            : Cancel(command, userId);

        return Task.FromResult(result);
    }

    private ToolResult<BookingResult> Create(BookingCommand command, ToolContext context, Guid passengerId)
    {
        if (command.TripId == Guid.Empty)
            return InvalidParameter("tripId", "A trip identifier is required");

        if (command.SeatCount is not int seatCount)
            return InvalidParameter("seatCount", "A seat count is required");

        if (seatCount < 1)
            return Fail("INVALID_SEAT_COUNT", "Seat count must be at least one", "seatCount");

        if (command.BookingForOthers is not bool bookingForOthers)
            return InvalidParameter("bookingForOthers", "Specify whether the booking is for others");

        if (seatCount > 1 && !bookingForOthers)
            return Fail("BOOKING_FOR_OTHERS_REQUIRED", "Booking multiple seats requires this flag", "bookingForOthers");

        if (context.JobId is not Guid jobId || jobId == Guid.Empty)
            return InvalidParameter("jobId", "A booking job identifier is required");

        if (string.IsNullOrWhiteSpace(context.LeaseToken))
            return InvalidParameter("leaseToken", "A lease token is required");

        var trip = session.FindTrip(command.TripId);
        if (trip is null)
            return Fail("TRIP_NOT_FOUND", "The trip does not exist", "tripId");

        if (trip.Status != TripStatus.Active)
            return Fail("TRIP_NOT_ACTIVE", "The trip is completed or cancelled", "tripId");

        if (trip.DriverId == passengerId)
            return Fail("SELF_BOOKING", "The driver cannot book seats in their own trip", "tripId");

        if (trip.RemainingSeats < seatCount)
            return Fail("INSUFFICIENT_SEATS", "The trip does not have enough available seats", "seatCount");

        var booking = new Booking(Guid.NewGuid(), trip.Id, jobId, passengerId, seatCount,
            BookingForOthers: bookingForOthers);
        var remainingSeats = trip.RemainingSeats - seatCount;
        session.Save(booking);
        session.Save(trip with { RemainingSeats = remainingSeats });

        return Success(booking, remainingSeats);
    }

    private ToolResult<BookingResult> Cancel(BookingCommand command, Guid userId)
    {
        if (command.BookingId is not Guid bookingId || bookingId == Guid.Empty)
            return InvalidParameter("bookingId", "A booking identifier is required");

        var booking = session.FindBooking(bookingId);
        if (booking is null)
            return Fail("BOOKING_NOT_FOUND", "The booking does not exist", "bookingId");

        var trip = session.FindTrip(booking.TripId);
        if (trip is null)
            return Fail("TRIP_NOT_FOUND", "The trip does not exist", "tripId");

        var byPassenger = command.Operation == BookingOperation.CancelByPassenger;
        var ownerId = byPassenger ? booking.PassengerId : trip.DriverId;
        if (userId != ownerId)
            return Fail("FORBIDDEN", "The user cannot cancel this booking", "bookingId");

        if (booking.Status != BookingJobStatus.Confirmed)
            return Fail("INVALID_BOOKING_STATE", "The booking is already completed or cancelled", "bookingId");

        var job = session.FindJob(booking.JobId)
            ?? throw new InvalidOperationException("The booking job is missing");
        var error = byPassenger ? job.CancelByPassenger() : job.CancelByDriver();
        if (error is not null)
            return new ToolResult<BookingResult>.Failure(error);

        var cancelledBooking = booking with
        {
            Status = byPassenger ? BookingJobStatus.CancelledByPassenger : BookingJobStatus.CancelledByDriver
        };
        var remainingSeats = checked(trip.RemainingSeats + booking.SeatCount);
        session.Save(job);
        session.Save(cancelledBooking);
        session.Save(trip with { RemainingSeats = remainingSeats });

        return Success(cancelledBooking, remainingSeats);
    }

    private static ToolResult<BookingResult> Success(Booking booking, int remainingSeats) =>
        new ToolResult<BookingResult>.Success(
            new BookingResult(booking.Id, booking.TripId, booking.Status.ToString(), booking.SeatCount, remainingSeats));

    private static ToolResult<BookingResult> InvalidParameter(string field, string message) =>
        Fail("INVALID_PARAMETER", message, field);

    private static ToolResult<BookingResult> Fail(string code, string message, string field) =>
        new ToolResult<BookingResult>.Failure(new ToolError(code, message, field));
}
