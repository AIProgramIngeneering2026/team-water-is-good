using NVKDrive.Application.Tools;
using NVKDrive.Domain.Bookings;

namespace NVKDrive.Application.Storage;

public interface IToolTransaction : IToolSession, IAsyncDisposable
{
    ToolError? Commit(Guid? jobId = null, Func<BookingJob, ToolError?>? update = null);
}