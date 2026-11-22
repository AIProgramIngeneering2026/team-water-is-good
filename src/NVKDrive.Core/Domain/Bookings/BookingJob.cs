namespace NVKDrive.Core;

public enum BookingJobStatus
{
    Created,
    Processing,
    Confirmed,
    Failed,
    Completed,
    CancelledByPassenger,
    CancelledByDriver,
    CancelledBecauseTripCancelled
}

public sealed record JobLease(Guid WorkerId, string Token, DateTimeOffset ExpiresAt);

public sealed class BookingJob(Guid id)
{
    public Guid Id { get; } = id;
    public BookingJobStatus Status { get; private set; } = BookingJobStatus.Created;
    public JobLease? Lease { get; private set; }
    public BookingJobError? Error { get; private set; }

    public BookingJob Copy() => new(Id)
    {
        Status = Status,
        Lease = Lease,
        Error = Error
    };

    public BookingJobError? Start(JobLease lease, DateTimeOffset now)
    {
        var rejection = BookingJobTransitions.ValidateStart(Status, Lease, lease, now);
        if (rejection is not null)
            return rejection;

        Lease = lease;
        Status = BookingJobStatus.Processing;
        return null;
    }

    public BookingJobError? Confirm(string leaseToken, DateTimeOffset now) =>
        FinishProcessing(BookingJobStatus.Confirmed, leaseToken, now);

    public BookingJobError? Fail(string leaseToken, DateTimeOffset now, BookingJobError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return FinishProcessing(BookingJobStatus.Failed, leaseToken, now, error);
    }

    public BookingJobError? ExpireLease(DateTimeOffset now)
    {
        var rejection = BookingJobTransitions.ValidateExpiry(Status, Lease, now);
        if (rejection is not null)
            return rejection;

        ChangeStatus(
            BookingJobStatus.Failed,
            new BookingJobError("PROCESSING_INTERRUPTED", "The processing lease expired."));
        return null;
    }

    public BookingJobError? Complete() => CloseConfirmed(BookingJobStatus.Completed);

    public BookingJobError? CancelByPassenger() => CloseConfirmed(BookingJobStatus.CancelledByPassenger);

    public BookingJobError? CancelByDriver() => CloseConfirmed(BookingJobStatus.CancelledByDriver);

    public BookingJobError? CancelBecauseTripCancelled() =>
        CloseConfirmed(BookingJobStatus.CancelledBecauseTripCancelled);

    private BookingJobError? FinishProcessing(
        BookingJobStatus nextStatus,
        string leaseToken,
        DateTimeOffset now,
        BookingJobError? error = null)
    {
        var rejection = BookingJobTransitions.ValidateFinish(Status, Lease, nextStatus, leaseToken, now);
        if (rejection is not null)
            return rejection;

        ChangeStatus(nextStatus, error);
        return null;
    }

    private BookingJobError? CloseConfirmed(BookingJobStatus nextStatus)
    {
        var rejection = BookingJobTransitions.ValidateClose(Status, nextStatus);
        if (rejection is not null)
            return rejection;

        ChangeStatus(nextStatus);
        return null;
    }

    private void ChangeStatus(BookingJobStatus status, BookingJobError? error = null)
    {
        Status = status;
        Lease = null;
        Error = error;
    }
}
