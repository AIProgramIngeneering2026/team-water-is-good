using NVKDrive.Application.Storage;
using NVKDrive.Application.Tools;
using NVKDrive.Domain.Bookings;
using NVKDrive.Domain.Trips;

namespace NVKDrive.Application.Trips;

public sealed class TripCompletionTool(IToolSession session, TimeProvider timeProvider)
    : ITool<TripCompletionCommand, TripCompletionResult>
{
    public Task<ToolResult<TripCompletionResult>> ExecuteAsync(
        TripCompletionCommand command,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Complete(command, context, cancellationToken));
    }

    private ToolResult<TripCompletionResult> Complete(
        TripCompletionCommand command,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Reason))
            return InvalidParameter("reason", "Unknown completion reason");

        if (command.TripId == Guid.Empty)
            return InvalidParameter("tripId", "A trip identifier is required");

        var isManual = command.Reason == CompletionReason.Manual;
        if (isManual && (context.UserId is null || context.UserId == Guid.Empty))
            return InvalidParameter("userId", "Manual completion requires a user identifier");

        if (!isManual && context.UserId is not null)
            return InvalidParameter("userId", "Automatic completion requires a system context");

        var trip = session.FindTrip(command.TripId);
        if (trip is null)
            return Fail("TRIP_NOT_FOUND", "The trip does not exist", "tripId");

        if (isManual && context.UserId != trip.DriverId)
            return Fail("FORBIDDEN", "Only the driver can complete this trip manually", "tripId");

        if (trip.Status != TripStatus.Active)
            return Fail("INVALID_TRIP_STATE", "The trip is already completed or cancelled", "tripId");

        var now = timeProvider.GetUtcNow();
        if (!isManual)
        {
            var tripTime = trip.ArrivalTime ?? trip.DepartureTime
                ?? throw new InvalidOperationException("The trip has no arrival or departure time");
            if (now - tripTime < TimeSpan.FromHours(2))
                return Fail("COMPLETION_TIME_NOT_REACHED", "The automatic completion time has not been reached",
                    "tripId");
        }

        var confirmedBookings = session.GetBookings(trip.Id)
            .Where(booking => booking.Status == BookingJobStatus.Confirmed)
            .ToArray();

        foreach (var booking in confirmedBookings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = session.FindJob(booking.JobId)
                      ?? throw new InvalidOperationException("The booking job is missing");
            var error = job.Complete();
            if (error is not null)
                return error.ToToolError().ToFailure<TripCompletionResult>();

            session.Save(job);
            session.Save(booking with { Status = BookingJobStatus.Completed });
        }

        session.Save(trip with { Status = TripStatus.Completed, CompletedAt = now });
        return new ToolResult<TripCompletionResult>.Success(
            new TripCompletionResult(trip.Id, nameof(TripStatus.Completed), now, confirmedBookings.Length));
    }

    private static ToolResult<TripCompletionResult> InvalidParameter(string field, string message)
    {
        return ToolErrorExtensions.InvalidParameter(field, message).ToFailure<TripCompletionResult>();
    }

    private static ToolResult<TripCompletionResult> Fail(string code, string message, string field)
    {
        return new ToolError(code, message, field).ToFailure<TripCompletionResult>();
    }
}