using System.Buffers;
using System.Security.Cryptography;
using Breakbar.Api.Persistence;
using Breakbar.Api.Options;
using Breakbar.Processing.Queue;

namespace Breakbar.Api.Jobs;

/// <summary>Result of one upload: HTTP status plus either the job or an error message.</summary>
public sealed record UploadOutcome(int StatusCode, JobDto? Job, string? Error)
{
    public static UploadOutcome Fail(int statusCode, string error) => new(statusCode, null, error);
}

/// <summary>
/// Accepts one log file as a raw request body: streams it to disk while hashing (single write on the HDD),
/// validates it, answers duplicates from the existing report, otherwise stores a job and enqueues it.
/// </summary>
public sealed class UploadService(
    JobStore store,
    FairParseQueue queue,
    ParseCostEstimator estimator,
    ParseQueueOptions queueOptions,
    JobEventHub hub,
    StorageOptions storage,
    UploadOptions uploadOptions,
    ReportsOptions reports)
{
    private static readonly string[] SupportedExtensions = [".evtc.zip", ".zevtc", ".evtc"];

    /// <summary>
    /// Largest uncompressed EVTC worth accepting: the configured limit, capped by what the memory budget can parse
    /// (≈ 53 MB with the default 2.4 GB limit and 150 MB + 42× estimate).
    /// </summary>
    private long MaxEvtcBytes => Math.Min(
        uploadOptions.MaxEvtcBytes,
        (long)((queueOptions.MaxJobMemoryBytes - queueOptions.EstimateBaseBytes) / queueOptions.EstimateBytesPerEvtcByte));

    public async Task<UploadOutcome> HandleAsync(string userId, string? fileName, string? batchId, bool anonymize, Stream body, CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(fileName ?? "");
        string? extension = SupportedExtensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        if (extension is null || name.Length > 200)
        {
            return UploadOutcome.Fail(StatusCodes.Status400BadRequest, "Expected a file name ending in .evtc, .zevtc or .evtc.zip (query parameter 'name').");
        }
        if (batchId is not null && (batchId.Length > 64 || !batchId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
        {
            return UploadOutcome.Fail(StatusCodes.Status400BadRequest, "Invalid batch id.");
        }

        // Capacity checks before accepting any bytes: rotating cookies or IPs must not fill disk or memory.
        if (CheckCapacity(userId) is { } refused)
        {
            return refused;
        }

        var jobId = Guid.NewGuid();
        Directory.CreateDirectory(storage.UploadsDirectory);
        string uploadPath = Path.GetFullPath(Path.Combine(storage.UploadsDirectory, jobId.ToString("N") + extension.ToLowerInvariant()));
        bool keepFile = false;
        try
        {
            var (fileBytes, sha256) = await SaveAndHashAsync(body, uploadPath, cancellationToken).ConfigureAwait(false);
            if (fileBytes < 0)
            {
                return UploadOutcome.Fail(StatusCodes.Status413PayloadTooLarge, $"File exceeds {uploadOptions.MaxUploadBytes / (1024 * 1024)} MB.");
            }

            long evtcBytes;
            try
            {
                evtcBytes = InspectEvtc(uploadPath, MaxEvtcBytes);
            }
            catch (LogTooLargeException)
            {
                return UploadOutcome.Fail(StatusCodes.Status413PayloadTooLarge, "Log is too large for this server.");
            }
            catch (InvalidDataException ex)
            {
                return UploadOutcome.Fail(StatusCodes.Status422UnprocessableEntity, "Not a valid arcdps log: " + ex.Message);
            }

            long now = store.Now();

            // Same file parsed before (e.g. several group members upload the same fight): no parse needed.
            if (store.FindReport(sha256, anonymize) is { } existing)
            {
                store.Insert(NewJob(jobId, userId, batchId, name, uploadPath: "", sha256, fileBytes, evtcBytes, 0, JobStatus.Done, existing.Id, now) with { FinishedAt = now, Anonymized = anonymize });
                var doneView = store.GetView(jobId.ToString("N"))!;
                var doneDto = JobDto.From(doneView, null, reports);
                hub.PublishJob(userId, doneDto);
                return new UploadOutcome(StatusCodes.Status200OK, doneDto, null);
            }

            long estimate = estimator.EstimateMemoryBytes(evtcBytes);
            if (estimate > queueOptions.MaxJobMemoryBytes)
            {
                return UploadOutcome.Fail(StatusCodes.Status413PayloadTooLarge, "Log is too large for this server (memory limit).");
            }

            string reportId = ShortId.New();
            store.Insert(NewJob(jobId, userId, batchId, name, uploadPath, sha256, fileBytes, evtcBytes, estimate, JobStatus.Queued, reportId, now) with { Anonymized = anonymize });
            keepFile = true;

            // Publish "queued" before enqueueing: a free worker may start the job immediately, and its
            // "running" event must not overtake this one.
            hub.PublishJob(userId, JobDto.From(store.GetView(jobId.ToString("N"))!, null, reports));
            queue.Enqueue(new ParseJob(jobId, userId, uploadPath, Path.GetFullPath(storage.ReportDirectory(reportId)), estimate, anonymize));
            hub.PublishPositions();

            var dto = JobDto.From(store.GetView(jobId.ToString("N"))!, queue.GetPosition(jobId), reports);
            return new UploadOutcome(StatusCodes.Status202Accepted, dto, null);
        }
        finally
        {
            if (!keepFile)
            {
                File.Delete(uploadPath);
            }
        }
    }

    private static JobRecord NewJob(Guid id, string userId, string? batchId, string fileName, string uploadPath, string sha256,
        long fileBytes, long evtcBytes, long estimate, string status, string reportId, long now) => new()
    {
        Id = id.ToString("N"),
        UserId = userId,
        BatchId = batchId,
        FileName = fileName,
        UploadPath = uploadPath,
        Sha256 = sha256,
        FileBytes = fileBytes,
        EvtcBytes = evtcBytes,
        EstimateBytes = estimate,
        Status = status,
        ReportId = reportId,
        CreatedAt = now,
    };

    /// <summary>Copies the body to disk and hashes it in one pass. Returns -1 as size if the limit was exceeded.</summary>
    private async Task<(long Bytes, string Sha256)> SaveAndHashAsync(Stream body, string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        long total = 0;
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > uploadOptions.MaxUploadBytes)
                {
                    return (-1, "");
                }
                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return (total, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private UploadOutcome? CheckCapacity(string userId)
    {
        if (!Identity.UserIdentity.IsServiceClient(userId) && queue.GetQueuedCount(userId) >= uploadOptions.MaxQueuedJobsPerUser)
        {
            return UploadOutcome.Fail(StatusCodes.Status429TooManyRequests,
                $"You already have {uploadOptions.MaxQueuedJobsPerUser} logs waiting. Please upload more once some are done.");
        }
        if (queue.GetStats().Queued >= uploadOptions.MaxQueuedJobsTotal)
        {
            return UploadOutcome.Fail(StatusCodes.Status503ServiceUnavailable, "The server is busy, please try again later.");
        }
        Directory.CreateDirectory(storage.DataDirectory);
        if (new DriveInfo(Path.GetFullPath(storage.DataDirectory)).AvailableFreeSpace < uploadOptions.MinFreeDiskBytes)
        {
            return UploadOutcome.Fail(StatusCodes.Status503ServiceUnavailable, "The server is out of storage space, please try again later.");
        }
        return null;
    }

    /// <summary>
    /// Verifies the log and returns its real uncompressed EVTC size. For zip archives the entry is fully decompressed
    /// (with a hard limit): the size stored in the zip directory can be forged, and Elite Insights decompresses the
    /// whole entry into memory, so a "zip bomb" would otherwise exhaust RAM.
    /// </summary>
    /// <exception cref="InvalidDataException">Not an EVTC log or a corrupt/forged archive.</exception>
    /// <exception cref="LogTooLargeException">Uncompressed data exceeds <paramref name="maxBytes"/>.</exception>
    private static long InspectEvtc(string path, long maxBytes)
    {
        if (path.EndsWith(".evtc", StringComparison.OrdinalIgnoreCase))
        {
            using var file = File.OpenRead(path);
            RequireEvtcMagic(file);
            return file.Length <= maxBytes ? file.Length : throw new LogTooLargeException();
        }

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        if (archive.Entries.Count != 1)
        {
            throw new InvalidDataException($"expected exactly one file in the archive, found {archive.Entries.Count}.");
        }
        var entry = archive.Entries[0];
        if (entry.Length > maxBytes)
        {
            throw new LogTooLargeException();
        }
        // Deflate never grows data by more than ~0.03 %. A compressed size well above the declared uncompressed
        // size means forged headers (.NET stops reading at the declared size, so this is the only visible trace).
        if (entry.CompressedLength > entry.Length + (entry.Length / 100) + 1024)
        {
            throw new InvalidDataException("archive size information is inconsistent.");
        }

        using var data = entry.Open();
        RequireEvtcMagic(data);
        long total = 4;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = data.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new LogTooLargeException();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return total == entry.Length ? total : throw new InvalidDataException("archive size information does not match its content.");
    }

    private static void RequireEvtcMagic(Stream stream)
    {
        Span<byte> magic = stackalloc byte[4];
        if (stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4 || !magic.SequenceEqual("EVTC"u8))
        {
            throw new InvalidDataException("missing EVTC header.");
        }
    }

    private sealed class LogTooLargeException : Exception;
}
