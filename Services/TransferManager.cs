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
            job.Status = TransferStatus.Failed;
            job.ErrorMessage = ex.Message;
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
        bool wasRunning = job.Status == TransferStatus.Running;
        job.Status = TransferStatus.Cancelled;
        job.Cts.Cancel();
        job.NotifyChanged();

        if (wasRunning && job.Direction == TransferDirection.Upload && job.UploadId != null)
        {
            var uploadId = job.UploadId;
            job.UploadId = null;
            _ = _s3.AbortMultipartUploadAsync(job.Bucket, job.Key, uploadId);
        }
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
