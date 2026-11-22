using NVKDrive.Application.Bookings;
using NVKDrive.Application.Dispatching;
using NVKDrive.Application.Tools;
using NVKDrive.Application.Trips;
using NVKDrive.Domain.Bookings;
using NVKDrive.Domain.Trips;
using NVKDrive.Infrastructure.Storage;

namespace NVKDrive.Core.Tests.Integration;

public sealed class CoreFlowTests
{
    private readonly Trip trip = new(Guid.NewGuid(), Guid.NewGuid(), 2, DateTimeOffset.UtcNow, null);
    private readonly Guid passenger = Guid.NewGuid();

    [Fact]
    public async Task Booking_then_completion_closes_job_and_booking()
    {
        var job = new BookingJob(Guid.NewGuid());
        var (store, dispatcher) = CreateCore(job);
        Assert.Equal(BookingJobStatus.Created, dispatcher.ReadJob(job.Id)!.Status);

        var booked = Assert.IsType<ToolResult<BookingResult>.Success>(await Book(dispatcher, job, 1));
        Assert.Equal(1, booked.Value.RemainingSeats);
        Assert.Equal(BookingJobStatus.Confirmed, dispatcher.ReadJob(job.Id)!.Status);

        var completion = await dispatcher.DispatchAsync<TripCompletionCommand, TripCompletionResult>(
            "trip-completion", trip.Id, new TripCompletionCommand(trip.Id, CompletionReason.Manual), trip.DriverId);
        Assert.Equal(1,
            Assert.IsType<ToolResult<TripCompletionResult>.Success>(completion).Value.CompletedBookingsCount);
        Assert.Equal(BookingJobStatus.Completed, dispatcher.ReadJob(job.Id)!.Status);
        await using var session = await store.BeginAsync(trip.Id, CancellationToken.None);
        Assert.Equal(TripStatus.Completed, session.FindTrip(trip.Id)!.Status);
        Assert.Equal(BookingJobStatus.Completed, session.FindBooking(booked.Value.BookingId)!.Status);
        Assert.Equal(1, session.FindTrip(trip.Id)!.RemainingSeats);
    }

    [Fact]
    public async Task Concurrent_booking_of_last_seats_has_one_winner()
    {
        var first = new BookingJob(Guid.NewGuid());
        var second = new BookingJob(Guid.NewGuid());
        var (store, dispatcher) = CreateCore(first, second);

        var results = await Task.WhenAll(
            Task.Run(() => Book(dispatcher, first, 2)),
            Task.Run(() => Book(dispatcher, second, 2)));

        Assert.Single(results.OfType<ToolResult<BookingResult>.Success>());
        Assert.Equal("INSUFFICIENT_SEATS",
            Assert.Single(results.OfType<ToolResult<BookingResult>.Failure>()).Error.Code);
        await using var session = await store.BeginAsync(trip.Id, CancellationToken.None);
        Assert.Equal(0, session.FindTrip(trip.Id)!.RemainingSeats);
        Assert.Single(session.GetBookings(trip.Id));
    }

    [Fact]
    public async Task Cancellation_restores_seats_once()
    {
        var job = new BookingJob(Guid.NewGuid());
        var (store, dispatcher) = CreateCore(job);
        var booking = Assert.IsType<ToolResult<BookingResult>.Success>(await Book(dispatcher, job, 2)).Value;
        var cancel = new BookingCommand(BookingOperation.CancelByPassenger, Guid.Empty, booking.BookingId, null, null);

        var first = await dispatcher.DispatchAsync<BookingCommand, BookingResult>("booking", Guid.Empty, cancel,
            passenger);
        var second =
            await dispatcher.DispatchAsync<BookingCommand, BookingResult>("booking", Guid.Empty, cancel, passenger);

        Assert.Equal(2, Assert.IsType<ToolResult<BookingResult>.Success>(first).Value.RemainingSeats);
        Assert.Equal("INVALID_BOOKING_STATE", Assert.IsType<ToolResult<BookingResult>.Failure>(second).Error.Code);
        Assert.Equal(BookingJobStatus.CancelledByPassenger, dispatcher.ReadJob(job.Id)!.Status);
        await using var session = await store.BeginAsync(trip.Id, CancellationToken.None);
        Assert.Equal(2, session.FindTrip(trip.Id)!.RemainingSeats);
    }

    private Task<ToolResult<BookingResult>> Book(Dispatcher dispatcher, BookingJob job, int seats)
    {
        return dispatcher.DispatchBookingAsync<BookingCommand, BookingResult>(
            "booking", trip.Id, job.Id, new BookingCommand(BookingOperation.Create, trip.Id, null, seats, seats > 1),
            passenger);
    }

    private (InMemoryCoreStore Store, Dispatcher Dispatcher) CreateCore(params BookingJob[] jobs)
    {
        var store = new InMemoryCoreStore([trip], jobs);
        var registry = new ToolRegistry();
        registry.Register<BookingCommand, BookingResult>("booking", session => new BookingTool(session));
        registry.Register<TripCompletionCommand, TripCompletionResult>(
            "trip-completion", session => new TripCompletionTool(session, TimeProvider.System));
        return (store, new Dispatcher(registry, store, TimeProvider.System, TimeSpan.FromMinutes(1)));
    }
}