using NVKDrive.Application.Tools;
using NVKDrive.Domain.Bookings;

namespace NVKDrive.Application.Storage;

public interface ICoreStore
{
    BookingJob? FindJob(Guid jobId);
    ToolError? UpdateJob(Guid jobId, Func<BookingJob, ToolError?> update);
    ValueTask<IToolTransaction> BeginAsync(Guid tripId, CancellationToken cancellationToken);
}