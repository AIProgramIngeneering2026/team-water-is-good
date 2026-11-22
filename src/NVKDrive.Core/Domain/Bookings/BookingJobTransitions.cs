namespace NVKDrive.Core;

internal static class BookingJobTransitions
{
    internal static BookingJobError? ValidateStart(
        BookingJobStatus status, JobLease? currentLease, JobLease lease, DateTimeOffset now)
    {
        if (status == BookingJobStatus.Processing)
        {
            return now >= currentLease!.ExpiresAt
                ? ExpiredLeaseError()
                : ProcessingError();
        }

        if (status != BookingJobStatus.Created)
        {
            return TransitionError(status, BookingJobStatus.Processing);
        }

        if (lease.WorkerId == Guid.Empty)
        {
            return new BookingJobError("INVALID_PARAMETER", "A worker identifier is required.", "workerId");
        }

        if (string.IsNullOrWhiteSpace(lease.Token))
        {
            return new BookingJobError("INVALID_PARAMETER", "A lease token is required.", "leaseToken");
        }

        if (now >= lease.ExpiresAt)
        {
            return ExpiredLeaseError();
        }

        return null;
    }

    internal static BookingJobError? ValidateExpiry(BookingJobStatus status, JobLease? currentLease, DateTimeOffset now)
    {
        if (status != BookingJobStatus.Processing)
        {
            return TransitionError(status, BookingJobStatus.Failed);
        }

        if (now < currentLease!.ExpiresAt)
        {
            return ProcessingError();
        }

        return null;
    }

    internal static BookingJobError? ValidateFinish(
        BookingJobStatus status, JobLease? currentLease, BookingJobStatus nextStatus,
        string leaseToken, DateTimeOffset now)
    {
        if (status != BookingJobStatus.Processing)
        {
            return TransitionError(status, nextStatus);
        }

        if (now >= currentLease!.ExpiresAt || !string.Equals(currentLease.Token, leaseToken, StringComparison.Ordinal))
        {
            return ExpiredLeaseError();
        }

        return null;
    }

    internal static BookingJobError? ValidateClose(BookingJobStatus status, BookingJobStatus nextStatus)
    {
        if (status == BookingJobStatus.Processing && nextStatus != BookingJobStatus.Completed)
        {
            return ProcessingError();
        }

        if (status != BookingJobStatus.Confirmed)
        {
            return TransitionError(status, nextStatus);
        }

        return null;
    }

    private static BookingJobError TransitionError(BookingJobStatus status, BookingJobStatus nextStatus)
    {
        if (status is BookingJobStatus.Failed
            or BookingJobStatus.Completed
            or BookingJobStatus.CancelledByPassenger
            or BookingJobStatus.CancelledByDriver
            or BookingJobStatus.CancelledBecauseTripCancelled)
        {
            return new BookingJobError("JOB_ALREADY_TERMINAL", "The booking job is already terminal.", "status");
        }

        return new BookingJobError(
            "INVALID_JOB_TRANSITION",
            $"Transition from '{status}' to '{nextStatus}' is not allowed.",
            "status");
    }

    private static BookingJobError ProcessingError() =>
        new("JOB_ALREADY_PROCESSING", "The booking job is already processing.", "status");

    private static BookingJobError ExpiredLeaseError() =>
        new("JOB_LEASE_EXPIRED", "The processing lease is invalid or expired.", "leaseToken");
}
