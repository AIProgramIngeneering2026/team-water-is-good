using NVKDrive.Application.Storage;
using NVKDrive.Application.Tools;
using NVKDrive.Domain.Bookings;
using NVKDrive.Domain.Trips;

namespace NVKDrive.Infrastructure.Storage;

public sealed class InMemoryCoreStore : ICoreStore
{
    private readonly object sync = new();
    private readonly SemaphoreSlim sessionGate = new(1);
    private readonly Dictionary<Guid, Trip> trips;
    private readonly Dictionary<Guid, Booking> bookings = new();
    private readonly Dictionary<Guid, BookingJob> jobs;

    public InMemoryCoreStore(IEnumerable<Trip>? trips = null, IEnumerable<BookingJob>? jobs = null)
    {
        this.trips = (trips ?? []).ToDictionary(trip => trip.Id);
        this.jobs = (jobs ?? []).ToDictionary(job => job.Id, job => job.Copy());
    }

    public BookingJob? FindJob(Guid jobId)
    {
        lock (sync)
        {
            return jobs.GetValueOrDefault(jobId)?.Copy();
        }
    }

    public ToolError? UpdateJob(Guid jobId, Func<BookingJob, ToolError?> update)
    {
        lock (sync)
        {
            var job = jobs.GetValueOrDefault(jobId)?.Copy();
            if (job is null)
                return JobNotFound();

            var error = update(job);
            if (error is null)
                jobs[jobId] = job.Copy();

            return error;
        }
    }

    public async ValueTask<IToolTransaction> BeginAsync(Guid tripId, CancellationToken cancellationToken)
    {
        // A single gate also covers commands that find the trip by booking ID.
        await sessionGate.WaitAsync(cancellationToken);
        return new Session(this);
    }

    private static ToolError JobNotFound()
    {
        return new ToolError("JOB_NOT_FOUND", "Booking job was not found.", "jobId");
    }

    private sealed class Session(InMemoryCoreStore store) : IToolTransaction
    {
        private readonly Dictionary<Guid, Trip> trips = new();
        private readonly Dictionary<Guid, Booking> bookings = new();
        private readonly Dictionary<Guid, BookingJob> jobs = new();
        private bool committed;
        private bool disposed;

        public Trip? FindTrip(Guid tripId)
        {
            return trips.GetValueOrDefault(tripId) ?? store.trips.GetValueOrDefault(tripId);
        }

        public Booking? FindBooking(Guid bookingId)
        {
            return bookings.GetValueOrDefault(bookingId) ?? store.bookings.GetValueOrDefault(bookingId);
        }

        public BookingJob? FindJob(Guid jobId)
        {
            return jobs.GetValueOrDefault(jobId)?.Copy() ?? store.FindJob(jobId);
        }

        public IEnumerable<Booking> GetBookings(Guid tripId)
        {
            return bookings.Values.Concat(store.bookings.Values.Where(booking => !bookings.ContainsKey(booking.Id)))
                .Where(booking => booking.TripId == tripId)
                .ToArray();
        }

        public void Save(Trip trip)
        {
            trips[trip.Id] = trip;
        }

        public void Save(Booking booking)
        {
            bookings[booking.Id] = booking;
        }

        public void Save(BookingJob job)
        {
            jobs[job.Id] = job.Copy();
        }

        public ToolError? Commit(Guid? jobId = null, Func<BookingJob, ToolError?>? update = null)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (committed)
                throw new InvalidOperationException("The session is already committed.");

            lock (store.sync)
            {
                if (jobId is Guid claimedId)
                {
                    var job = store.jobs.GetValueOrDefault(claimedId)?.Copy();
                    if (job is null)
                        return JobNotFound();

                    var error = (update ?? throw new ArgumentNullException(nameof(update)))(job);
                    if (error is not null)
                        return error;

                    jobs[claimedId] = job.Copy();
                }

                foreach (var (id, trip) in trips)
                    store.trips[id] = trip;
                foreach (var (id, booking) in bookings)
                    store.bookings[id] = booking;
                foreach (var (id, job) in jobs)
                    store.jobs[id] = job;

                committed = true;
                return null;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                store.sessionGate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}