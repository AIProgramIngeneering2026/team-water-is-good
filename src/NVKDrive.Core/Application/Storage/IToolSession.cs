using NVKDrive.Domain.Bookings;
using NVKDrive.Domain.Trips;

namespace NVKDrive.Application.Storage;

public interface IToolSession
{
    Trip? FindTrip(Guid tripId);
    Booking? FindBooking(Guid bookingId);
    BookingJob? FindJob(Guid jobId);
    IEnumerable<Booking> GetBookings(Guid tripId);
    void Save(Trip trip);
    void Save(Booking booking);
    void Save(BookingJob job);
}