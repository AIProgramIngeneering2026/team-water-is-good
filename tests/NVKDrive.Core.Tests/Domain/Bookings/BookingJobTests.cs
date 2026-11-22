using NVKDrive.Domain.Bookings;

namespace NVKDrive.Core.Tests.Domain.Bookings;

public sealed class BookingJobTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly JobLease Lease = new(Guid.NewGuid(), "worker-token", Now.AddMinutes(1));

    [Fact]
    public void RestartedJobExpiresAndRejectsLateSuccess()
    {
        var storedJob = StartJob().Copy();

        Assert.Equal("JOB_ALREADY_PROCESSING", storedJob.ExpireLease(Now)?.Code);
        Assert.Equal(BookingJobStatus.Processing, storedJob.Status);
        Assert.Equal("JOB_LEASE_EXPIRED", storedJob.Confirm(Lease.Token, Lease.ExpiresAt)?.Code);
        Assert.Equal(BookingJobStatus.Processing, storedJob.Status);

        Assert.Null(storedJob.ExpireLease(Lease.ExpiresAt));
        Assert.Equal(BookingJobStatus.Failed, storedJob.Status);
        Assert.Equal("PROCESSING_INTERRUPTED", storedJob.Error?.Code);
        Assert.Null(storedJob.Lease);
        Assert.Equal("JOB_ALREADY_TERMINAL", storedJob.Confirm(Lease.Token, Lease.ExpiresAt)?.Code);
    }

    [Theory]
    [InlineData(BookingJobStatus.CancelledByPassenger)]
    [InlineData(BookingJobStatus.CancelledByDriver)]
    [InlineData(BookingJobStatus.CancelledBecauseTripCancelled)]
    public void CancelledJobRejectsLateSuccess(BookingJobStatus cancellation)
    {
        var job = StartJob();
        Assert.Equal("JOB_ALREADY_PROCESSING", job.CancelByPassenger()?.Code);
        Assert.Null(job.Confirm(Lease.Token, Now));

        var error = cancellation switch
        {
            BookingJobStatus.CancelledByPassenger => job.CancelByPassenger(),
            BookingJobStatus.CancelledByDriver => job.CancelByDriver(),
            BookingJobStatus.CancelledBecauseTripCancelled => job.CancelBecauseTripCancelled(),
            _ => throw new ArgumentOutOfRangeException(nameof(cancellation))
        };

        Assert.Null(error);
        Assert.Equal("JOB_ALREADY_TERMINAL", job.Confirm(Lease.Token, Now)?.Code);
        Assert.Equal(cancellation, job.Status);
    }

    [Fact]
    public void FailedJobRejectsSecondOutcome()
    {
        var job = StartJob();
        var failure = new BookingJobError("INSUFFICIENT_SEATS", "The trip is full.", "seatCount");

        Assert.Null(job.Fail(Lease.Token, Now, failure));
        Assert.Equal("JOB_ALREADY_TERMINAL", job.Confirm(Lease.Token, Now)?.Code);
        Assert.Equal("JOB_ALREADY_TERMINAL", job.Start(Lease, Now)?.Code);
        Assert.Equal(BookingJobStatus.Failed, job.Status);
        Assert.Equal(failure, job.Error);
    }

    [Fact]
    public void CreatedJobCannotSucceedWithoutBeingStarted()
    {
        var job = new BookingJob(Guid.NewGuid());

        Assert.Equal("INVALID_JOB_TRANSITION", job.Confirm(Lease.Token, Now)?.Code);
        Assert.Equal("INVALID_JOB_TRANSITION", job.Complete()?.Code);
        Assert.Equal(BookingJobStatus.Created, job.Status);
    }

    [Fact]
    public void SuccessfulJobCannotStartAgain()
    {
        var job = StartJob();
        Assert.Null(job.Confirm(Lease.Token, Now));

        Assert.Equal("INVALID_JOB_TRANSITION", job.Start(Lease, Now)?.Code);
        Assert.Equal(BookingJobStatus.Confirmed, job.Status);
        Assert.Null(job.Complete());
        Assert.Equal("JOB_ALREADY_TERMINAL", job.Start(Lease, Now)?.Code);
        Assert.Equal(BookingJobStatus.Completed, job.Status);
    }

    [Fact]
    public void OldWorkerCannotFinishWithWrongToken()
    {
        var job = StartJob();

        Assert.Equal("JOB_LEASE_EXPIRED", job.Confirm("other-token", Now)?.Code);
        Assert.Equal("JOB_LEASE_EXPIRED", job.Fail("other-token", Now, new BookingJobError("ERROR", "Error."))?.Code);
        Assert.Equal(BookingJobStatus.Processing, job.Status);
        Assert.Equal(Lease, job.Lease);
    }

    private static BookingJob StartJob()
    {
        var job = new BookingJob(Guid.NewGuid());
        Assert.Null(job.Start(Lease, Now));
        return job;
    }
}