namespace S3Lite.Models;

public enum TransferDirection { Upload, Download }

public enum TransferStatus { Pending, Running, Paused, Completed, Failed, Cancelled }

public class CompletedPart
{
    public int PartNumber { get; set; }
    public string ETag { get; set; } = "";
}

public class TransferJob
{
    public Guid Id { get; } = Guid.NewGuid();
    public TransferDirection Direction { get; init; }
    public string FileName { get; init; } = "";
    public string Bucket { get; init; } = "";
    public string Key { get; init; } = "";
    public string LocalPath { get; init; } = "";
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public int Progress => TotalBytes > 0 ? (int)(TransferredBytes * 100 / TotalBytes) : 0;
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public string? ErrorMessage { get; set; }

    // Set by MainForm on the UI thread — no threading issues
    public double SpeedBytesPerSec { get; set; }

    // Multipart concurrency display
    public int ActiveParts;  // Interlocked — parts actively sending right now
    public int TotalParts;   // set once when multipart begins

    // Multipart upload resume state (kept in memory within session)
    public string? UploadId { get; set; }
    public List<CompletedPart> CompletedParts { get; set; } = new();
    public int NextPartNumber => CompletedParts.Count + 1;

    public CancellationTokenSource Cts { get; private set; } = new();

    public event Action? Changed;

    private int _lastProgress = -1;
    private TransferStatus _lastStatus = (TransferStatus)(-1);

    public void NotifyChanged()
    {
        int p = Progress;
        if (p != _lastProgress || Status != _lastStatus)
        {
            _lastProgress = p;
            _lastStatus = Status;
            Changed?.Invoke();
        }
        else
        {
            Changed?.Invoke();
        }
    }

    public void ResetCts()
    {
        Cts.Dispose();
        Cts = new CancellationTokenSource();
    }
}
