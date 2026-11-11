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
    UploadOptions uploadOptions)
{
    private static readonly string[] SupportedExtensions = [".evtc.zip", ".zevtc", ".evtc"];

    public async Task<UploadOutcome> HandleAsync(string userId, string? fileName, string? batchId, Stream body, CancellationToken cancellationToken)
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
                evtcBytes = ReadEvtcSize(uploadPath);
            }
            catch (InvalidDataException ex)
            {
                return UploadOutcome.Fail(StatusCodes.Status422UnprocessableEntity, "Not a valid arcdps log: " + ex.Message);
            }
            if (evtcBytes > uploadOptions.MaxEvtcBytes)
            {
                return UploadOutcome.Fail(StatusCodes.Status413PayloadTooLarge, "Log is too large to process.");
            }

            long now = store.Now();

            // Same file parsed before (e.g. several group members upload the same fight): no parse needed.
            if (store.FindReportBySha(sha256) is { } existing)
            {
                store.Insert(NewJob(jobId, userId, batchId, name, uploadPath: "", sha256, fileBytes, evtcBytes, 0, JobStatus.Done, existing.Id, now) with { FinishedAt = now });
                var doneView = store.GetView(jobId.ToString("N"))!;
                var doneDto = JobDto.From(doneView, null);
                hub.PublishJob(userId, doneDto);
                return new UploadOutcome(StatusCodes.Status200OK, doneDto, null);
            }

            long estimate = estimator.EstimateMemoryBytes(evtcBytes);
            if (estimate > queueOptions.MaxJobMemoryBytes)
            {
                return UploadOutcome.Fail(StatusCodes.Status413PayloadTooLarge, "Log is too large for this server (memory limit).");
            }

            string reportId = ShortId.New();
            store.Insert(NewJob(jobId, userId, batchId, name, uploadPath, sha256, fileBytes, evtcBytes, estimate, JobStatus.Queued, reportId, now));
            keepFile = true;

            // Publish "queued" before enqueueing: a free worker may start the job immediately, and its
            // "running" event must not overtake this one.
            hub.PublishJob(userId, JobDto.From(store.GetView(jobId.ToString("N"))!, null));
            queue.Enqueue(new ParseJob(jobId, userId, uploadPath, Path.GetFullPath(storage.ReportDirectory(reportId)), estimate));
            hub.PublishPositions();

            var dto = JobDto.From(store.GetView(jobId.ToString("N"))!, queue.GetPosition(jobId));
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

    /// <summary>Uncompressed EVTC size; checks the "EVTC" magic bytes of plain logs and the zip structure of compressed ones.</summary>
    private static long ReadEvtcSize(string path)
    {
        if (path.EndsWith(".evtc", StringComparison.OrdinalIgnoreCase))
        {
            Span<byte> magic = stackalloc byte[4];
            using var file = File.OpenRead(path);
            if (file.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4 || !magic.SequenceEqual("EVTC"u8))
            {
                throw new InvalidDataException("missing EVTC header.");
            }
            return file.Length;
        }
        return ParseCostEstimator.GetUncompressedEvtcBytes(path);
    }
}
