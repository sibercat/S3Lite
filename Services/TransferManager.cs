using S3Lite.Models;

namespace S3Lite.Services;

public class TransferManager : IDisposable
{
    private readonly S3Service _s3;
    private readonly SemaphoreSlim _uploadSlots;
    private readonly SemaphoreSlim _downloadSlots;
    private readonly List<TransferJob> _jobs = new();
    private readonly object _jobsLock = new();

    public event Action<TransferJob>? JobAdded;
    public event Action<TransferJob>? JobChanged;

    public TransferManager(S3Service s3, int maxUploads = 3, int maxDownloads = 3, int parallelParts = 4)
    {
        _s3            = s3;
        _uploadSlots   = new SemaphoreSlim(maxUploads,   maxUploads);
        _downloadSlots = new SemaphoreSlim(maxDownloads, maxDownloads);
        s3.ParallelPartsPerUpload = parallelParts;
    }

    public IReadOnlyList<TransferJob> Jobs
    {
        get { lock (_jobsLock) return _jobs.ToList().AsReadOnly(); }
    }

    public TransferJob Enqueue(TransferDirection direction, string bucket, string key, string localPath)
    {
        var job = new TransferJob
        {
            Direction = direction,
            FileName = Path.GetFileName(localPath),
            Bucket = bucket,
            Key = key,
            LocalPath = localPath
        };
        job.Changed += () => JobChanged?.Invoke(job);
        lock (_jobsLock) _jobs.Add(job);
        JobAdded?.Invoke(job);
        _ = RunAsync(job);
        return job;
    }

    private async Task RunAsync(TransferJob job)
    {
        var slots = job.Direction == TransferDirection.Upload ? _uploadSlots : _downloadSlots;

        try
        {
            await slots.WaitAsync(job.Cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            job.NotifyChanged();
            return;
        }

        try
        {
            job.Status = TransferStatus.Running;
            job.NotifyChanged();

            if (job.Direction == TransferDirection.Upload)
                await _s3.UploadJobAsync(job, job.Cts.Token).ConfigureAwait(false);
            else
                await _s3.DownloadJobAsync(job, job.Cts.Token).ConfigureAwait(false);

            if (job.Status == TransferStatus.Running)
            {
                job.TransferredBytes = job.TotalBytes;
                job.Status = TransferStatus.Completed;
            }
            job.NotifyChanged();
        }
        catch (OperationCanceledException)
        {
            if (job.Status == TransferStatus.Running)
                job.Status = TransferStatus.Paused;
            job.NotifyChanged();
        }
        catch (Exception ex)
        {
            // A cancelled job's abort can make in-flight parts throw NoSuchUpload —
            // keep the Cancelled status rather than reporting a failure
            if (job.Status != TransferStatus.Cancelled)
            {
                job.Status = TransferStatus.Failed;
                job.ErrorMessage = ex.Message;
            }
            job.NotifyChanged();
        }
        finally
        {
            slots.Release();
        }
    }

    public void Pause(TransferJob job)
    {
        if (job.Status != TransferStatus.Running && job.Status != TransferStatus.Pending) return;
        job.Status = TransferStatus.Paused;
        job.Cts.Cancel();
        job.NotifyChanged();
    }

    public void Resume(TransferJob job)
    {
        if (job.Status != TransferStatus.Paused && job.Status != TransferStatus.Failed) return;
        job.ResetCts();
        job.Status = TransferStatus.Pending;
        job.NotifyChanged();
        _ = RunAsync(job);
    }

    public void Cancel(TransferJob job)
    {
        job.Status = TransferStatus.Cancelled;
        job.Cts.Cancel();
        job.NotifyChanged();

        // Abort any in-progress multipart upload regardless of prior state
        // (running, paused, or failed) — orphaned parts accrue storage costs.
        // CompletedParts is deliberately not cleared: in-flight part tasks may
        // still be appending to it under _partsLock, and a Cancelled job is
        // terminal so the list is never read again.
        _ = AbortIfMultipartAsync(job);
    }

    private Task AbortIfMultipartAsync(TransferJob job)
    {
        if (job.Direction != TransferDirection.Upload || job.UploadId == null)
            return Task.CompletedTask;
        var uploadId = job.UploadId;
        job.UploadId = null;
        return _s3.AbortMultipartUploadAsync(job.Bucket, job.Key, uploadId);
    }

    /// <summary>
    /// Cancels jobs and waits for their multipart aborts to reach S3. Used on
    /// shutdown, where firing the aborts and immediately disposing the client
    /// would cancel the requests and leave the parts orphaned.
    /// </summary>
    public async Task CancelAndAbortAsync(IEnumerable<TransferJob> jobs)
    {
        var aborts = new List<Task>();
        foreach (var job in jobs)
        {
            job.Status = TransferStatus.Cancelled;
            job.Cts.Cancel();
            job.NotifyChanged();
            aborts.Add(AbortIfMultipartAsync(job));
        }
        // Don't hang shutdown indefinitely if S3 is unreachable
        await Task.WhenAny(Task.WhenAll(aborts), Task.Delay(TimeSpan.FromSeconds(8)))
                  .ConfigureAwait(false);
    }

    public void ClearCompleted()
    {
        lock (_jobsLock)
            _jobs.RemoveAll(j => j.Status is TransferStatus.Completed or TransferStatus.Cancelled);
    }

    public void Dispose()
    {
        _uploadSlots.Dispose();
        _downloadSlots.Dispose();
    }
}
