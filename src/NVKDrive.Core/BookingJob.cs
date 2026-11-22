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
    public ToolError? Error { get; private set; }

    public BookingJob Copy() => new(Id)
    {
        Status = Status,
        Lease = Lease,
        Error = Error
    };

    public ToolError? Start(JobLease lease, DateTimeOffset now)
    {
        if (Status == BookingJobStatus.Processing)
        {
            return now >= Lease!.ExpiresAt
                ? ExpiredLeaseError()
                : ProcessingError();
        }

        if (Status != BookingJobStatus.Created)
        {
            return TransitionError(BookingJobStatus.Processing);
        }

        if (lease.WorkerId == Guid.Empty)
        {
            return new ToolError("INVALID_PARAMETER", "A worker identifier is required.", "workerId");
        }

        if (string.IsNullOrWhiteSpace(lease.Token))
        {
            return new ToolError("INVALID_PARAMETER", "A lease token is required.", "leaseToken");
        }

        if (now >= lease.ExpiresAt)
        {
            return ExpiredLeaseError();
        }

        Lease = lease;
        Status = BookingJobStatus.Processing;
        return null;
    }

    public ToolError? Confirm(string leaseToken, DateTimeOffset now) =>
        FinishProcessing(BookingJobStatus.Confirmed, leaseToken, now);

    public ToolError? Fail(string leaseToken, DateTimeOffset now, ToolError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return FinishProcessing(BookingJobStatus.Failed, leaseToken, now, error);
    }

    public ToolError? ExpireLease(DateTimeOffset now)
    {
        if (Status != BookingJobStatus.Processing)
        {
            return TransitionError(BookingJobStatus.Failed);
        }

        if (now < Lease!.ExpiresAt)
        {
            return ProcessingError();
        }

        ChangeStatus(
            BookingJobStatus.Failed,
            new ToolError("PROCESSING_INTERRUPTED", "The processing lease expired."));
        return null;
    }

    public ToolError? Complete() => CloseConfirmed(BookingJobStatus.Completed);

    public ToolError? CancelByPassenger() => CloseConfirmed(BookingJobStatus.CancelledByPassenger);

    public ToolError? CancelByDriver() => CloseConfirmed(BookingJobStatus.CancelledByDriver);

    public ToolError? CancelBecauseTripCancelled() =>
        CloseConfirmed(BookingJobStatus.CancelledBecauseTripCancelled);

    private ToolError? FinishProcessing(
        BookingJobStatus nextStatus,
        string leaseToken,
        DateTimeOffset now,
        ToolError? error = null)
    {
        if (Status != BookingJobStatus.Processing)
        {
            return TransitionError(nextStatus);
        }

        if (now >= Lease!.ExpiresAt || !string.Equals(Lease.Token, leaseToken, StringComparison.Ordinal))
        {
            return ExpiredLeaseError();
        }

        ChangeStatus(nextStatus, error);
        return null;
    }

    private ToolError? CloseConfirmed(BookingJobStatus nextStatus)
    {
        if (Status == BookingJobStatus.Processing && nextStatus != BookingJobStatus.Completed)
        {
            return ProcessingError();
        }

        if (Status != BookingJobStatus.Confirmed)
        {
            return TransitionError(nextStatus);
        }

        ChangeStatus(nextStatus);
        return null;
    }

    private void ChangeStatus(BookingJobStatus status, ToolError? error = null)
    {
        Status = status;
        Lease = null;
        Error = error;
    }

    private ToolError TransitionError(BookingJobStatus nextStatus)
    {
        if (Status is BookingJobStatus.Failed
            or BookingJobStatus.Completed
            or BookingJobStatus.CancelledByPassenger
            or BookingJobStatus.CancelledByDriver
            or BookingJobStatus.CancelledBecauseTripCancelled)
        {
            return new ToolError("JOB_ALREADY_TERMINAL", "The booking job is already terminal.", "status");
        }

        return new ToolError(
            "INVALID_JOB_TRANSITION",
            $"Transition from '{Status}' to '{nextStatus}' is not allowed.",
            "status");
    }

    private static ToolError ProcessingError() =>
        new("JOB_ALREADY_PROCESSING", "The booking job is already processing.", "status");

    private static ToolError ExpiredLeaseError() =>
        new("JOB_LEASE_EXPIRED", "The processing lease is invalid or expired.", "leaseToken");
}
