using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using S3Lite.Models;
using System.IO.Compression;

// Alias to avoid conflict with S3Lite.Models.CompletedPart
using AwsPartETag = Amazon.S3.Model.PartETag;

namespace S3Lite.Services;

public record CreateBucketOptions(
    bool    BlockPublicAccess,
    bool    DisableAcls,
    bool    ObjectLock,
    string? LockMode,   // "GOVERNANCE", "COMPLIANCE", or null
    int?    LockDays,
    int?    LockYears
);

public class S3Service : IDisposable
{
    private AmazonS3Client _client;
    private readonly S3Connection _connection;

    public S3Service(S3Connection conn)
    {
        _connection = conn;

        AWSCredentials creds = conn.CredentialType switch
        {
            "EnvVars"    => new EnvironmentVariablesAWSCredentials(),
            "AwsProfile" => ResolveProfileCredentials(conn.AwsProfileName),
            _            => new BasicAWSCredentials(conn.AccessKey, conn.SecretKey)
        };

        var config = new AmazonS3Config
        {
            ForcePathStyle        = conn.ForcePathStyle,
            UseDualstackEndpoint  = conn.UseDualStack,
            UseAccelerateEndpoint = conn.UseAcceleration,
        };

        if (!string.IsNullOrWhiteSpace(conn.EndpointUrl))
            config.ServiceURL = conn.EndpointUrl;
        else
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(conn.Region);

        _client = new AmazonS3Client(creds, config);
    }

    private static AWSCredentials ResolveProfileCredentials(string profileName)
    {
        var chain = new CredentialProfileStoreChain();
        if (chain.TryGetAWSCredentials(profileName, out var creds))
            return creds;
        throw new Exception(
            $"AWS profile '{profileName}' not found.\n" +
            $"Check your ~/.aws/credentials or AWS credentials store.");
    }

    // ── Per-bucket regional clients (external buckets may be in a different region) ──
    private readonly Dictionary<string, AmazonS3Client> _extraClients = new();

    private AmazonS3Client ClientFor(string bucket)
        => _extraClients.TryGetValue(bucket, out var c) ? c : _client;

    /// <summary>Detect the AWS region for any accessible bucket.</summary>
    public async Task<string> GetBucketRegionAsync(string bucket)
    {
        var resp = await _client.GetBucketLocationAsync(
            new GetBucketLocationRequest { BucketName = bucket }).ConfigureAwait(false);
        var loc = resp.Location?.Value;
        return string.IsNullOrEmpty(loc) ? "us-east-1" : loc;
    }

    /// <summary>Register an external bucket with a known region, creating a dedicated regional client.</summary>
    public void RegisterExternalBucket(string bucket, string region)
    {
        if (_extraClients.ContainsKey(bucket)) return;
        var creds  = new BasicAWSCredentials(_connection.AccessKey, _connection.SecretKey);
        var config = new AmazonS3Config
        {
            ForcePathStyle = _connection.ForcePathStyle,
            RegionEndpoint = RegionEndpoint.GetBySystemName(region)
        };
        _extraClients[bucket] = new AmazonS3Client(creds, config);
    }

    public async Task<List<string>> ListBucketsAsync()
    {
        var resp = await _client.ListBucketsAsync();
        return resp.Buckets.Select(b => b.BucketName).ToList();
    }

    /// <summary>Flat list of every file in the bucket (no folder markers).</summary>
    public Task<List<S3Item>> ListAllObjectsAsync(string bucket) =>
        ListAllObjectsAsync(bucket, prefix: null);

    public async Task<List<S3Item>> ListAllObjectsAsync(string bucket, string? prefix)
    {
        var items = new List<S3Item>();
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix ?? "" };
        ListObjectsV2Response resp;
        do
        {
            resp = await ClientFor(bucket).ListObjectsV2Async(request).ConfigureAwait(false);
            foreach (var obj in resp.S3Objects ?? [])
            {
                if (obj.Key.EndsWith('/')) continue; // skip folder markers
                items.Add(new S3Item
                {
                    Type         = S3ItemType.File,
                    Name         = Path.GetFileName(obj.Key),
                    Key          = obj.Key,
                    Size         = obj.Size ?? 0,
                    LastModified = obj.LastModified ?? DateTime.MinValue,
                    StorageClass = obj.StorageClass?.Value ?? "STANDARD"
                });
            }
            request.ContinuationToken = resp.NextContinuationToken;
        } while (resp.IsTruncated == true);
        return items;
    }

    public async Task<List<S3Item>> ListObjectsAsync(string bucket, string prefix)
    {
        var items = new List<S3Item>();
        var request = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix     = prefix,
            Delimiter  = "/",
            MaxKeys    = ListPageSize ?? 1000
        };

        ListObjectsV2Response resp;
        do
        {
            resp = await ClientFor(bucket).ListObjectsV2Async(request);

            foreach (var cp in resp.CommonPrefixes ?? [])
            {
                if (string.IsNullOrEmpty(cp)) continue;
                var folderName = cp.TrimEnd('/');
                if (prefix.Length > 0)
                    folderName = folderName[prefix.Length..];
                items.Add(new S3Item { Type = S3ItemType.Folder, Name = folderName, Key = cp });
            }

            foreach (var obj in resp.S3Objects ?? [])
            {
                if (obj.Key == prefix) continue;
                var fileName = obj.Key[prefix.Length..];
                items.Add(new S3Item
                {
                    Type         = S3ItemType.File,
                    Name         = fileName,
                    Key          = obj.Key,
                    Size         = obj.Size ?? 0,
                    LastModified = obj.LastModified ?? DateTime.MinValue,
                    StorageClass = obj.StorageClass?.Value ?? "STANDARD"
                });
            }

            request.ContinuationToken = resp.NextContinuationToken;
        } while (resp.IsTruncated == true && ListPageSize == null); // stop after one page if pagination enabled

        return items;
    }

    public async Task UploadFileAsync(string bucket, string key, string localPath,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(localPath);
        long totalBytes = fileInfo.Length;
        long uploadedBytes = 0;

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            FilePath = localPath,
            StreamTransferProgress = (_, e) =>
            {
                uploadedBytes = e.TransferredBytes;
                progress?.Report((int)(uploadedBytes * 100 / totalBytes));
            }
        };

        await ClientFor(bucket).PutObjectAsync(request, ct);
    }

public async Task DownloadFileAsync(string bucket, string key, string localPath,
        Action<int>? onProgress = null, CancellationToken ct = default)
    {
        var request = new GetObjectRequest { BucketName = bucket, Key = key };
        using var resp = await ClientFor(bucket).GetObjectAsync(request, ct).ConfigureAwait(false);

        long total = resp.ContentLength;
        long downloaded = 0;
        int lastReported = -1;
        var buffer = new byte[262144];

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        using var src = WrapDecompress(resp.ResponseStream, resp.Headers.ContentEncoding);
        using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            if (total > 0)
            {
                int pct = (int)(downloaded * 100 / total);
                if (pct != lastReported) { lastReported = pct; onProgress?.Invoke(pct); }
            }
        }
    }

    /// <summary>Server-side copy/move all objects under srcPrefix to dstBucket/dstPrefix.</summary>
    public async Task CopyPrefixAsync(
        string srcBucket, string srcPrefix,
        string dstBucket, string dstPrefix,
        bool deleteSource,
        IProgress<(int done, int total, string key)>? progress = null,
        CancellationToken ct = default)
    {
        // Collect all keys under source
        var keys = new List<string>();
        var listReq = new ListObjectsV2Request { BucketName = srcBucket, Prefix = srcPrefix };
        ListObjectsV2Response listResp;
        do
        {
            listResp = await ClientFor(srcBucket).ListObjectsV2Async(listReq, ct).ConfigureAwait(false);
            foreach (var obj in listResp.S3Objects ?? [])
                if (obj.Key != srcPrefix) // skip folder marker itself
                    keys.Add(obj.Key);
            listReq.ContinuationToken = listResp.NextContinuationToken;
        } while (listResp.IsTruncated == true);

        for (int i = 0; i < keys.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string srcKey  = keys[i];
            string relKey  = srcPrefix.Length > 0 ? srcKey[srcPrefix.Length..] : srcKey;
            string dstKey  = dstPrefix + relKey;

            await ClientFor(srcBucket).CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket      = srcBucket,
                SourceKey         = srcKey,
                DestinationBucket = dstBucket,
                DestinationKey    = dstKey
            }, ct).ConfigureAwait(false);

            if (deleteSource)
                await ClientFor(srcBucket).DeleteObjectAsync(srcBucket, srcKey).ConfigureAwait(false);

            progress?.Report((i + 1, keys.Count, srcKey));
        }
    }

    public async Task DeleteBucketAsync(string bucket)
    {
        // Empty the bucket first (delete all object versions)
        var listReq = new ListObjectsV2Request { BucketName = bucket };
        ListObjectsV2Response listResp;
        do
        {
            listResp = await ClientFor(bucket).ListObjectsV2Async(listReq).ConfigureAwait(false);
            foreach (var obj in listResp.S3Objects ?? [])
                await ClientFor(bucket).DeleteObjectAsync(bucket, obj.Key).ConfigureAwait(false);
            listReq.ContinuationToken = listResp.NextContinuationToken;
        } while (listResp.IsTruncated == true);

        await ClientFor(bucket).DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket })
            .ConfigureAwait(false);
    }

    public async Task DeleteObjectAsync(string bucket, string key)
    {
        await ClientFor(bucket).DeleteObjectAsync(bucket, key);
    }

    public async Task RenameObjectAsync(string bucket, string oldKey, string newKey)
    {
        await ClientFor(bucket).CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket      = bucket,
            SourceKey         = oldKey,
            DestinationBucket = bucket,
            DestinationKey    = newKey
        }).ConfigureAwait(false);
        await ClientFor(bucket).DeleteObjectAsync(bucket, oldKey).ConfigureAwait(false);
    }

    /// <summary>Wraps a response stream in the appropriate decompression stream if Content-Encoding requires it.</summary>
    private static Stream WrapDecompress(Stream stream, string? contentEncoding)
    {
        if (string.IsNullOrEmpty(contentEncoding)) return stream;
        if (contentEncoding.Contains("gzip",    StringComparison.OrdinalIgnoreCase))
            return new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        if (contentEncoding.Contains("br",      StringComparison.OrdinalIgnoreCase))
            return new System.IO.Compression.BrotliStream(stream, System.IO.Compression.CompressionMode.Decompress);
        if (contentEncoding.Contains("deflate", StringComparison.OrdinalIgnoreCase))
            return new System.IO.Compression.DeflateStream(stream, System.IO.Compression.CompressionMode.Decompress);
        return stream;
    }

    /// <summary>Downloads up to maxBytes of an object into memory for preview purposes.</summary>
    public async Task<(byte[] Data, long TotalSize, string ContentType)> GetObjectPreviewAsync(
        string bucket, string key, int maxBytes = 1024 * 1024, CancellationToken ct = default)
    {
        var request = new GetObjectRequest { BucketName = bucket, Key = key };
        using var resp = await ClientFor(bucket).GetObjectAsync(request, ct).ConfigureAwait(false);
        long total = resp.ContentLength;
        string ct2 = resp.Headers.ContentType ?? "";
        bool compressed = !string.IsNullOrEmpty(resp.Headers.ContentEncoding);
        // For compressed content we don't know the decompressed size — cap at maxBytes
        int toRead = compressed ? maxBytes : (int)Math.Min(total, maxBytes);
        using var stream = WrapDecompress(resp.ResponseStream, resp.Headers.ContentEncoding);
        var buf    = new byte[toRead];
        int offset = 0;
        while (offset < toRead)
        {
            int read = await stream.ReadAsync(buf.AsMemory(offset, toRead - offset), ct).ConfigureAwait(false);
            if (read == 0) break;
            offset += read;
        }
        return (buf[..offset], total, ct2);
    }

    /// <summary>Changes the storage class of a single object via server-side copy-to-self.</summary>
    public async Task ChangeObjectStorageClassAsync(string bucket, string key, string storageClass)
    {
        var sc = S3StorageClass.FindValue(storageClass);
        await ClientFor(bucket).CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket      = bucket,
            SourceKey         = key,
            DestinationBucket = bucket,
            DestinationKey    = key,
            StorageClass      = sc,
            MetadataDirective = S3MetadataDirective.COPY
        }).ConfigureAwait(false);
    }

    /// <summary>Changes the storage class of every object in the bucket via server-side copy-to-self.</summary>
    public async Task ChangeStorageClassAsync(string bucket, string storageClass,
        IProgress<(int done, int total)>? progress = null, CancellationToken ct = default)
    {
        // Collect all keys first
        var keys    = new List<string>();
        var listReq = new ListObjectsV2Request { BucketName = bucket };
        ListObjectsV2Response listResp;
        do
        {
            listResp = await ClientFor(bucket).ListObjectsV2Async(listReq, ct).ConfigureAwait(false);
            foreach (var obj in listResp.S3Objects ?? [])
                if (!obj.Key.EndsWith('/'))
                    keys.Add(obj.Key);
            listReq.ContinuationToken = listResp.NextContinuationToken;
        } while (listResp.IsTruncated == true);

        var sc = S3StorageClass.FindValue(storageClass);
        for (int i = 0; i < keys.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await ClientFor(bucket).CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket      = bucket,
                SourceKey         = keys[i],
                DestinationBucket = bucket,
                DestinationKey    = keys[i],
                StorageClass      = sc,
                MetadataDirective = S3MetadataDirective.COPY
            }, ct).ConfigureAwait(false);
            progress?.Report((i + 1, keys.Count));
        }
    }

    public async Task DeleteFolderAsync(string bucket, string prefix)
    {
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
        ListObjectsV2Response resp;
        do
        {
            resp = await ClientFor(bucket).ListObjectsV2Async(request);
            foreach (var obj in resp.S3Objects)
                await ClientFor(bucket).DeleteObjectAsync(bucket, obj.Key);
            request.ContinuationToken = resp.NextContinuationToken;
        } while (resp.IsTruncated == true);
    }

    public async Task CreateBucketAsync(string name, string region, CreateBucketOptions opts)
    {
        var req = new PutBucketRequest
        {
            BucketName                 = name,
            ObjectLockEnabledForBucket = opts.ObjectLock
        };

        bool isCustomEndpoint = !string.IsNullOrWhiteSpace(_connection.EndpointUrl);
        if (!isCustomEndpoint && region != "us-east-1" && !string.IsNullOrEmpty(region))
            req.BucketRegionName = region;

        await _client.PutBucketAsync(req).ConfigureAwait(false);

        if (opts.BlockPublicAccess)
        {
            try
            {
                await _client.PutPublicAccessBlockAsync(new PutPublicAccessBlockRequest
                {
                    BucketName = name,
                    PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
                    {
                        BlockPublicAcls       = true,
                        IgnorePublicAcls      = true,
                        BlockPublicPolicy     = true,
                        RestrictPublicBuckets = true
                    }
                }).ConfigureAwait(false);
            }
            catch { /* best-effort: non-AWS endpoints may not support this */ }
        }

        if (opts.DisableAcls)
        {
            try
            {
                await _client.PutBucketOwnershipControlsAsync(new PutBucketOwnershipControlsRequest
                {
                    BucketName       = name,
                    OwnershipControls = new OwnershipControls
                    {
                        Rules = new List<OwnershipControlsRule>
                        {
                            new() { ObjectOwnership = ObjectOwnership.BucketOwnerEnforced }
                        }
                    }
                }).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }

        if (opts.ObjectLock && opts.LockMode != null)
        {
            try
            {
                var mode = opts.LockMode == "GOVERNANCE"
                    ? ObjectLockRetentionMode.Governance
                    : ObjectLockRetentionMode.Compliance;

                var retention = new DefaultRetention { Mode = mode };
                if (opts.LockDays.HasValue)        retention.Days  = opts.LockDays.Value;
                else if (opts.LockYears.HasValue)  retention.Years = opts.LockYears.Value;

                await _client.PutObjectLockConfigurationAsync(new PutObjectLockConfigurationRequest
                {
                    BucketName            = name,
                    ObjectLockConfiguration = new ObjectLockConfiguration
                    {
                        ObjectLockEnabled = ObjectLockEnabled.Enabled,
                        Rule              = new ObjectLockRule { DefaultRetention = retention }
                    }
                }).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }
    }

    public async Task CreateFolderAsync(string bucket, string prefix)
    {
        if (!prefix.EndsWith('/')) prefix += '/';
        await ClientFor(bucket).PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = prefix,
            ContentBody = ""
        });
    }

    // ── Transfer queue methods ────────────────────────────────────────────────

    private const long PartSize = 8L * 1024 * 1024;  // 8 MB per part
    public int  ParallelPartsPerUpload  { get; set; } = 4;
    public long MultipartThresholdBytes { get; set; } = 16L * 1024 * 1024; // default 16 MB
    public List<CompressionRule> CompressionRules { get; set; } = new();
    public int?  ListPageSize           { get; set; } = null; // null = fetch all (no pagination cap)

    /// <summary>
    /// If a compression rule matches, compresses the file to a temp path and returns it.
    /// Returns null if no rule matched (upload original file as-is).
    /// Caller must delete the temp file after upload.
    /// </summary>
    private string? CompressIfNeeded(string bucket, string key, string localPath, out int level)
    {
        level = 6;
        var rule = CompressionRuleStore.FindMatch(CompressionRules, bucket, key);
        if (rule == null) return null;

        level = rule.Level;
        string tmp = Path.GetTempFileName();
        using var src  = File.OpenRead(localPath);
        using var dst  = File.Create(tmp);
        using var gz   = new GZipStream(dst, (CompressionLevel)MapLevel(rule.Level));
        src.CopyTo(gz);
        return tmp;
    }

    private static CompressionLevel MapLevel(int level) => level switch
    {
        1 or 2 => CompressionLevel.Fastest,
        9      => CompressionLevel.SmallestSize,
        _      => CompressionLevel.Optimal
    };

    public async Task UploadJobAsync(TransferJob job, CancellationToken ct)
    {
        // Apply compression rule if one matches
        string uploadPath    = job.LocalPath;
        long   originalSize  = new FileInfo(job.LocalPath).Length;
        string? tempCompressed = CompressIfNeeded(job.Bucket, job.Key, job.LocalPath, out _);
        bool compressed = tempCompressed != null;
        if (compressed) uploadPath = tempCompressed!;

        try
        {
            await UploadJobInternalAsync(job, uploadPath, compressed, compressed ? originalSize : 0, ct).ConfigureAwait(false);
        }
        finally
        {
            if (compressed && File.Exists(tempCompressed))
                File.Delete(tempCompressed!);
        }
    }

    private async Task UploadJobInternalAsync(TransferJob job, string uploadPath, bool compressed, long originalSize, CancellationToken ct)
    {
        var fileInfo = new FileInfo(uploadPath);
        job.TotalBytes = fileInfo.Length;

        if (fileInfo.Length < MultipartThresholdBytes)
        {
            // Small file — single PutObject
            var req = new PutObjectRequest
            {
                BucketName = job.Bucket,
                Key        = job.Key,
                FilePath   = uploadPath,
                StreamTransferProgress = (_, e) =>
                {
                    job.TransferredBytes = e.TransferredBytes;
                    job.NotifyChanged();
                }
            };
            if (compressed)
            {
                req.Headers.ContentEncoding = "gzip";
                req.Metadata["original-size"] = originalSize.ToString();
            }
            await ClientFor(job.Bucket).PutObjectAsync(req, ct).ConfigureAwait(false);
            return;
        }

        // Large file — parallel parts (supports pause/resume)
        if (job.UploadId == null)
        {
            var initReq = new InitiateMultipartUploadRequest { BucketName = job.Bucket, Key = job.Key };
            if (compressed)
            {
                initReq.Headers.ContentEncoding = "gzip";
                initReq.Metadata["original-size"] = originalSize.ToString();
            }
            var initResp = await ClientFor(job.Bucket).InitiateMultipartUploadAsync(initReq, ct).ConfigureAwait(false);
            job.UploadId = initResp.UploadId;
        }

        long fileSize   = fileInfo.Length;
        int  totalParts = (int)Math.Ceiling((double)fileSize / PartSize);
        job.TotalParts  = totalParts;
        job.ActiveParts = 0;

        // Parts already completed in a previous run (pause/resume)
        var completedNums = new HashSet<int>(job.CompletedParts.Select(p => p.PartNumber));
        long alreadyDone  = Math.Min((long)completedNums.Count * PartSize, fileSize);
        job.TransferredBytes = alreadyDone;

        // Per-part in-flight byte counters (array index = partNumber; long read/write is atomic on x64)
        var partFlight = new long[totalParts + 1];
        long runDone   = 0L; // bytes from parts that finished this run
        var partsLock  = new object();

        int n     = Math.Clamp(ParallelPartsPerUpload, 1, 128);
        var sem   = new SemaphoreSlim(n, n);
        var tasks = new List<Task>();

        for (int p = 1; p <= totalParts; p++)
        {
            if (completedNums.Contains(p)) continue;

            int  partNum    = p;
            long partOffset = (long)(partNum - 1) * PartSize;
            int  partLen    = (int)Math.Min(PartSize, fileSize - partOffset);

            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(ct).ConfigureAwait(false);
                Interlocked.Increment(ref job.ActiveParts);
                try
                {
                    var buffer = new byte[partLen];
                    using var fs2 = new FileStream(uploadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                    fs2.Seek(partOffset, SeekOrigin.Begin);
                    await fs2.ReadExactlyAsync(buffer, 0, partLen, ct).ConfigureAwait(false);

                    var resp = await ClientFor(job.Bucket).UploadPartAsync(new UploadPartRequest
                    {
                        BucketName = job.Bucket,
                        Key        = job.Key,
                        UploadId   = job.UploadId!,
                        PartNumber = partNum,
                        InputStream = new MemoryStream(buffer, 0, partLen),
                        StreamTransferProgress = (_, e) =>
                        {
                            partFlight[partNum] = e.TransferredBytes;
                            long inFlight = 0;
                            for (int i = 1; i <= totalParts; i++) inFlight += partFlight[i];
                            job.TransferredBytes = alreadyDone + Interlocked.Read(ref runDone) + inFlight;
                            job.NotifyChanged();
                        }
                    }, ct).ConfigureAwait(false);

                    // Part finished — zero its in-flight counter, add to run total
                    partFlight[partNum] = 0;
                    Interlocked.Add(ref runDone, partLen);
                    lock (partsLock)
                        job.CompletedParts.Add(new CompletedPart { PartNumber = partNum, ETag = resp.ETag });

                    job.TransferredBytes = alreadyDone + Interlocked.Read(ref runDone);
                    job.NotifyChanged();
                }
                finally
                {
                    Interlocked.Decrement(ref job.ActiveParts);
                    sem.Release();
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        await ClientFor(job.Bucket).CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = job.Bucket,
            Key        = job.Key,
            UploadId   = job.UploadId,
            PartETags  = job.CompletedParts
                .OrderBy(p => p.PartNumber)
                .Select(p => new AwsPartETag { PartNumber = p.PartNumber, ETag = p.ETag })
                .ToList()
        }, ct).ConfigureAwait(false);

        job.UploadId = null; // completed — no longer resumable
    }

    public async Task DownloadJobAsync(TransferJob job, CancellationToken ct)
    {
        string tempPath = job.LocalPath + ".s3lite_part";

        var request = new GetObjectRequest { BucketName = job.Bucket, Key = job.Key };
        using var resp = await ClientFor(job.Bucket).GetObjectAsync(request, ct).ConfigureAwait(false);

        job.TotalBytes = resp.ContentLength;
        job.TransferredBytes = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(job.LocalPath)!);

        using var src = WrapDecompress(resp.ResponseStream, resp.Headers.ContentEncoding);
        using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        var buffer = new byte[262144]; // 256 KB
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            job.TransferredBytes += read;
            job.NotifyChanged();
        }
        await fs.FlushAsync(ct).ConfigureAwait(false);
        fs.Close();

        if (File.Exists(job.LocalPath)) File.Delete(job.LocalPath);
        File.Move(tempPath, job.LocalPath);
    }

    /// <summary>Renames a folder (prefix) by copying all objects to the new prefix then deleting the originals.</summary>
    public async Task RenameFolderAsync(string bucket, string oldPrefix, string newPrefix, CancellationToken ct = default)
    {
        if (!oldPrefix.EndsWith('/')) oldPrefix += '/';
        if (!newPrefix.EndsWith('/')) newPrefix += '/';

        var keys    = new List<string>();
        var listReq = new ListObjectsV2Request { BucketName = bucket, Prefix = oldPrefix };
        ListObjectsV2Response listResp;
        do
        {
            listResp = await ClientFor(bucket).ListObjectsV2Async(listReq, ct).ConfigureAwait(false);
            foreach (var obj in listResp.S3Objects ?? [])
                keys.Add(obj.Key);
            listReq.ContinuationToken = listResp.NextContinuationToken;
        } while (listResp.IsTruncated == true);

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            string destKey = newPrefix + key[oldPrefix.Length..];
            await ClientFor(bucket).CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket      = bucket,
                SourceKey         = key,
                DestinationBucket = bucket,
                DestinationKey    = destKey,
                MetadataDirective = S3MetadataDirective.COPY
            }, ct).ConfigureAwait(false);
            await ClientFor(bucket).DeleteObjectAsync(bucket, key, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Initiates a Glacier restore request. Days = how long the restored copy stays available.</summary>
    public async Task RestoreObjectAsync(string bucket, string key, int days)
    {
        await ClientFor(bucket).RestoreObjectAsync(new RestoreObjectRequest
        {
            BucketName    = bucket,
            Key           = key,
            Days          = days,
            RetrievalTier = GlacierJobTier.Standard
        }).ConfigureAwait(false);
    }

    public async Task AbortMultipartUploadAsync(string bucket, string key, string uploadId)
    {
        try
        {
            await ClientFor(bucket).AbortMultipartUploadAsync(new AbortMultipartUploadRequest
            {
                BucketName = bucket,
                Key = key,
                UploadId = uploadId
            }).ConfigureAwait(false);
        }
        catch { /* best-effort */ }
    }

    // ── Bucket properties ─────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> RegionFriendlyNames = new()
    {
        ["us-east-1"]      = "US East (N. Virginia)",
        ["us-east-2"]      = "US East (Ohio)",
        ["us-west-1"]      = "US West (N. California)",
        ["us-west-2"]      = "US West (Oregon)",
        ["eu-west-1"]      = "Europe (Ireland)",
        ["eu-west-2"]      = "Europe (London)",
        ["eu-west-3"]      = "Europe (Paris)",
        ["eu-central-1"]   = "Europe (Frankfurt)",
        ["eu-north-1"]     = "Europe (Stockholm)",
        ["ap-northeast-1"] = "Asia Pacific (Tokyo)",
        ["ap-northeast-2"] = "Asia Pacific (Seoul)",
        ["ap-southeast-1"] = "Asia Pacific (Singapore)",
        ["ap-southeast-2"] = "Asia Pacific (Sydney)",
        ["ap-south-1"]     = "Asia Pacific (Mumbai)",
        ["sa-east-1"]      = "South America (São Paulo)",
        ["ca-central-1"]   = "Canada (Central)",
        ["cn-north-1"]     = "China (Beijing)",
        ["cn-northwest-1"] = "China (Ningxia)",
        ["us-gov-east-1"]  = "AWS GovCloud (US-East)",
        ["us-gov-west-1"]  = "AWS GovCloud (US-West)",
    };

    public async Task<BucketProperties> GetBucketPropertiesAsync(string bucket)
    {
        var props = new BucketProperties { Name = bucket };
        var c = ClientFor(bucket);

        // ── Run all config calls in parallel ──────────────────────────────────
        var ownerTask = Task.Run(async () =>
        {
            try
            {
                var resp = await _client.ListBucketsAsync().ConfigureAwait(false);
                props.OwnerId = resp.Owner?.Id ?? "";
                var b = resp.Buckets?.FirstOrDefault(x => x.BucketName == bucket);
                if (b != null) props.CreationDate = b.CreationDate;
            }
            catch { }
        });

        var regionTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetBucketLocationAsync(
                    new GetBucketLocationRequest { BucketName = bucket }).ConfigureAwait(false);
                string code = string.IsNullOrEmpty(resp.Location?.Value) ? "us-east-1" : resp.Location.Value;
                props.Region = RegionFriendlyNames.TryGetValue(code, out var name)
                    ? $"{name} ({code})" : code;
            }
            catch { }
        });

        var versioningTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetBucketVersioningAsync(
                    new GetBucketVersioningRequest { BucketName = bucket }).ConfigureAwait(false);
                var vs = resp.VersioningConfig?.Status?.ToString();
                props.Versioning = !string.IsNullOrEmpty(vs) ? vs : "Disabled";
            }
            catch { props.Versioning = "Disabled"; }
        });

        var loggingTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetBucketLoggingAsync(
                    new GetBucketLoggingRequest { BucketName = bucket }).ConfigureAwait(false);
                props.Logging = !string.IsNullOrEmpty(resp.BucketLoggingConfig?.TargetBucketName)
                    ? "Enabled" : "Disabled";
            }
            catch { props.Logging = "Disabled"; }
        });

        var objectLockTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetObjectLockConfigurationAsync(
                    new GetObjectLockConfigurationRequest { BucketName = bucket }).ConfigureAwait(false);
                props.ObjectLock = resp.ObjectLockConfiguration?.ObjectLockEnabled == ObjectLockEnabled.Enabled
                    ? "Enabled" : "Disabled";
            }
            catch { props.ObjectLock = "Disabled"; }
        });

        var accelerationTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetBucketAccelerateConfigurationAsync(
                    new GetBucketAccelerateConfigurationRequest { BucketName = bucket }).ConfigureAwait(false);
                props.TransferAcceleration = !string.IsNullOrEmpty(resp.Status) ? resp.Status : "Disabled";
            }
            catch { props.TransferAcceleration = "Disabled"; }
        });

        var encryptionTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetBucketEncryptionAsync(
                    new GetBucketEncryptionRequest { BucketName = bucket }).ConfigureAwait(false);
                var rule = resp.ServerSideEncryptionConfiguration?.ServerSideEncryptionRules?.FirstOrDefault();
                props.Encryption = rule != null
                    ? $"Enabled ({rule.ServerSideEncryptionByDefault?.ServerSideEncryptionAlgorithm?.Value ?? "AES256"})"
                    : "Disabled";
            }
            catch { props.Encryption = "Disabled"; }
        });

        var requesterTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetBucketRequestPaymentAsync(
                    new GetBucketRequestPaymentRequest { BucketName = bucket }).ConfigureAwait(false);
                props.RequesterPays = resp.Payer == "Requester" ? "Enabled" : "Disabled";
            }
            catch { props.RequesterPays = "Disabled"; }
        });

        var replicationTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.GetBucketReplicationAsync(
                    new GetBucketReplicationRequest { BucketName = bucket }).ConfigureAwait(false);
                props.Replication = resp.Configuration?.Rules?.Count > 0 ? "Enabled" : "Disabled";
            }
            catch { props.Replication = "Disabled"; }
        });

        var multipartTask = Task.Run(async () =>
        {
            try
            {
                var resp = await c.ListMultipartUploadsAsync(
                    new ListMultipartUploadsRequest { BucketName = bucket }).ConfigureAwait(false);
                props.UncompletedMultipartUploads = resp.MultipartUploads?.Count ?? 0;
            }
            catch { }
        });

        // ── List all objects for stats (runs in parallel with config calls) ───
        var statsTask = Task.Run(async () =>
        {
            try
            {
                var extCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var scCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                DateTime? minDate = null, maxDate = null;

                var req = new ListObjectsV2Request { BucketName = bucket };
                ListObjectsV2Response resp;
                do
                {
                    resp = await c.ListObjectsV2Async(req).ConfigureAwait(false);
                    foreach (var obj in resp.S3Objects ?? [])
                    {
                        if (obj.Key.EndsWith('/')) { props.TotalFolders++; continue; }
                        props.TotalFiles++;
                        props.TotalSize += obj.Size ?? 0;

                        var ext = Path.GetExtension(obj.Key).ToUpperInvariant();
                        if (!string.IsNullOrEmpty(ext))
                        {
                            extCounts.TryGetValue(ext, out int ec);
                            extCounts[ext] = ec + 1;
                        }

                        var sc = obj.StorageClass?.Value ?? "STANDARD";
                        scCounts.TryGetValue(sc, out int scc);
                        scCounts[sc] = scc + 1;

                        if (obj.LastModified != default)
                        {
                            if (minDate == null || obj.LastModified < minDate) minDate = obj.LastModified;
                            if (maxDate == null || obj.LastModified > maxDate) maxDate = obj.LastModified;
                        }
                    }
                    req.ContinuationToken = resp.NextContinuationToken;
                } while (resp.IsTruncated == true);

                props.TotalObjects = props.TotalFiles + props.TotalFolders;
                props.FileTypes    = extCounts.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key.TrimStart('.')} File ({kv.Value})").ToList();
                props.StorageClasses = scCounts
                    .Select(kv => $"{kv.Key} ({kv.Value})").ToList();
                props.ModifiedFrom = minDate;
                props.ModifiedTo   = maxDate;
            }
            catch { }
        });

        await Task.WhenAll(ownerTask, regionTask, versioningTask, loggingTask,
            objectLockTask, accelerationTask, encryptionTask, requesterTask,
            replicationTask, multipartTask, statsTask).ConfigureAwait(false);

        return props;
    }

    // ── ACL ───────────────────────────────────────────────────────────────────

    public async Task<GetObjectAclResponse> GetAclAsync(string bucket, string key)
    {
        return await ClientFor(bucket).GetObjectAclAsync(new GetObjectAclRequest { BucketName = bucket, Key = key })
            .ConfigureAwait(false);
    }

    public async Task PutAclAsync(string bucket, string key, S3AccessControlList acl)
    {
        await ClientFor(bucket).PutObjectAclAsync(new PutObjectAclRequest
        {
            BucketName = bucket,
            Key = key,
            AccessControlPolicy = acl
        }).ConfigureAwait(false);
    }

    // ── URL generation ────────────────────────────────────────────────────────

    public string GetPublicUrl(string bucket, string key, bool https = true)
    {
        string scheme = https ? "https" : "http";
        if (!string.IsNullOrWhiteSpace(_connection.EndpointUrl))
            return $"{_connection.EndpointUrl.TrimEnd('/')}/{bucket}/{key}";

        string region = string.IsNullOrWhiteSpace(_connection.Region) ? "us-east-1" : _connection.Region;
        return $"{scheme}://s3.dualstack.{region}.amazonaws.com/{bucket}/{key}";
    }

    public string DefaultHostname(string bucket, bool https = true)
    {
        string scheme = https ? "https" : "http";
        if (!string.IsNullOrWhiteSpace(_connection.EndpointUrl))
            return _connection.EndpointUrl.TrimEnd('/') + "/";
        string region = string.IsNullOrWhiteSpace(_connection.Region) ? "us-east-1" : _connection.Region;
        return $"{scheme}://s3.dualstack.{region}.amazonaws.com/{bucket}/";
    }

    public string GetPreSignedUrl(string bucket, string key, DateTime expiresAt, bool https = true)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key        = key,
            Expires    = expiresAt,
            Protocol   = https ? Protocol.HTTPS : Protocol.HTTP,
            Verb       = HttpVerb.GET
        };
        return ClientFor(bucket).GetPreSignedURL(request);
    }

    // ── File properties ───────────────────────────────────────────────────────

    public async Task<List<(string Prop, string Val, bool Highlight)>> GetFilePropertiesAsync(string bucket, string key)
    {
        var rows   = new List<(string, string, bool)>();
        var client = ClientFor(bucket);

        var meta = await client.GetObjectMetadataAsync(bucket, key).ConfigureAwait(false);

        // ── Identity ──────────────────────────────────────────────────────────
        string name = key.Contains('/') ? key[(key.LastIndexOf('/') + 1)..] : key;
        rows.Add(("Name",    name,             false));
        rows.Add(("S3 URI",  $"s3://{bucket}/{key}", false));
        if (key != name)
            rows.Add(("Full key", key,          false));
        rows.Add(("ETag",    meta.ETag?.Trim('"') ?? "—", false));

        // ── Storage ───────────────────────────────────────────────────────────
        rows.Add(("---", "", false));
        long size = meta.ContentLength;
        rows.Add(("Size",          $"{FormatBytes(size)} ({size:N0} bytes)",                        false));
        rows.Add(("Storage class", meta.StorageClass?.Value ?? "STANDARD",                          false));
        rows.Add(("Last modified", meta.LastModified.HasValue
            ? meta.LastModified.Value.ToLocalTime().ToString("M/d/yyyy h:mm:ss tt")
            : "—", false));

        if (!string.IsNullOrEmpty(meta.Headers.ContentType))
            rows.Add(("Content type", meta.Headers.ContentType, false));

        // ── Encryption ────────────────────────────────────────────────────────
        rows.Add(("---", "", false));
        string enc    = meta.ServerSideEncryptionMethod?.Value ?? "";
        bool   hasEnc = !string.IsNullOrEmpty(enc);
        rows.Add(("Server-side encrypted",
            hasEnc ? $"Yes, {EncryptionFriendlyName(enc)}" : "No",
            hasEnc));

        // ── Compression ───────────────────────────────────────────────────────
        string? contentEnc = meta.Headers.ContentEncoding;
        bool    compressed = !string.IsNullOrEmpty(contentEnc);

        string? origSizeStr = meta.Metadata.Keys.Contains("original-size")
            ? meta.Metadata["original-size"]
            : meta.Metadata.Keys.Contains("x-amz-meta-original-size")
                ? meta.Metadata["x-amz-meta-original-size"]
                : null;

        if (compressed && origSizeStr != null && long.TryParse(origSizeStr, out long origSize) && origSize > 0)
        {
            double ratio = (1.0 - (double)size / origSize) * 100.0;
            rows.Add(("Client-side compressed",
                $"Yes ({contentEnc!.ToUpperInvariant()}, compression ratio {ratio:F2}%)", true));
            rows.Add(("Original file size",
                $"{FormatBytes(origSize)} ({origSize:N0} bytes)", false));
        }
        else if (compressed)
        {
            rows.Add(("Client-side compressed", $"Yes ({contentEnc!.ToUpperInvariant()})", true));
        }
        else
        {
            rows.Add(("Client-side compressed", "No", false));
        }

        // ── Misc ─────────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.VersionId))
            rows.Add(("Version ID", meta.VersionId, false));
        if (!string.IsNullOrEmpty(meta.Headers.CacheControl))
            rows.Add(("Cache control", meta.Headers.CacheControl, false));

        // ── Owner (from ACL) ──────────────────────────────────────────────────
        try
        {
            var acl = await client.GetObjectAclAsync(
                new GetObjectAclRequest { BucketName = bucket, Key = key }).ConfigureAwait(false);
            if (acl.Owner != null)
            {
                string owner = !string.IsNullOrEmpty(acl.Owner.DisplayName)
                    ? acl.Owner.DisplayName
                    : acl.Owner.Id;
                rows.Add(("Owner", owner, false));
            }
        }
        catch { /* ACL may be inaccessible */ }

        // ── Custom metadata ───────────────────────────────────────────────────
        var customKeys = meta.Metadata.Keys
            .Where(k => k != "original-size" && k != "x-amz-meta-original-size")
            .ToList();
        if (customKeys.Count > 0)
        {
            rows.Add(("---", "", false));
            foreach (var k in customKeys)
            {
                string display = k.StartsWith("x-amz-meta-") ? k["x-amz-meta-".Length..] : k;
                rows.Add(($"Metadata: {display}", meta.Metadata[k], false));
            }
        }

        return rows;
    }

    private static string EncryptionFriendlyName(string enc) => enc switch
    {
        "aws:kms"        => "Server-Side Encryption with AWS KMS Keys (SSE-KMS)",
        "AES256"         => "Server-Side Encryption with Amazon S3-Managed Keys (SSE-S3)",
        "aws:kms:dsse"   => "Dual-layer Server-Side Encryption with AWS KMS Keys (DSSE-KMS)",
        _                => enc
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        if (bytes >= 1024 * 1024)         return $"{bytes / 1024.0 / 1024.0:F2} MB";
        if (bytes >= 1024)                return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }

    public void Dispose()
    {
        _client.Dispose();
        foreach (var c in _extraClients.Values) c.Dispose();
    }
}
