using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Persistence;

namespace QuickQr.Infrastructure.Batch;

/// <summary>
/// Encodes and renders many codes with bounded parallelism and streams them into a ZIP archive
/// (entries are written sequentially in input order) followed by <c>manifest.csv</c>.
/// Individual failures are collected; they never abort the batch. Cancellation throws <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class ZipBatchGenerator(
    IBarcodeEncoder encoder,
    IBarcodeRenderer renderer,
    IEntitlementService entitlements,
    ILogger<ZipBatchGenerator>? logger = null) : IBatchGenerator
{
    internal const string ManifestFileName = "manifest.csv";
    internal static readonly string[] ManifestHeader = ["file", "payload", "status", "error"];

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>Maximum renders in flight (also bounds memory held by rendered-but-unwritten images).</summary>
    internal int MaxDegreeOfParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 4);

    public async Task<Result<BatchResult>> GenerateZipAsync(
        BatchRequest request,
        Stream output,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);

        if (!entitlements.HasFeature(PremiumFeature.BatchGeneration))
            return Error.FeatureLocked($"Upgrade to use: {FeatureCatalog.Get(PremiumFeature.BatchGeneration).Name}.");
        if (request.Items.Count == 0)
            return Error.Validation("There is nothing to generate.");
        if (!output.CanWrite)
            return Error.Validation("The output stream is not writable.");

        cancellationToken.ThrowIfCancellationRequested();

        var total = request.Items.Count;
        var names = AssignFileNames(request.Items);
        var defaultExtension = ExtensionFor(request.RenderOptions.Format);
        var failures = new List<BatchFailure>();
        var manifest = new List<string?[]>(total);
        var succeeded = 0;
        var inFlight = new Queue<Task<ItemOutcome>>();
        var next = 0;

        progress?.Report(new BatchProgress(0, total, null));

        try
        {
            var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            await using (archive.ConfigureAwait(false))
            {
                for (var completed = 0; completed < total; completed++)
                {
                    while (next < total && inFlight.Count < MaxDegreeOfParallelism)
                    {
                        var index = next++;
                        inFlight.Enqueue(Task.Run(() => RenderItemAsync(request, index, cancellationToken), cancellationToken));
                    }

                    var outcome = await inFlight.Dequeue().ConfigureAwait(false);
                    var name = names[outcome.Index];

                    if (outcome.Image is { } image)
                    {
                        var entryName = name + image.FileExtension;
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                        await using (entryStream.ConfigureAwait(false))
                        {
                            await entryStream.WriteAsync(image.Data, cancellationToken).ConfigureAwait(false);
                        }

                        succeeded++;
                        manifest.Add([entryName, request.Items[outcome.Index].Payload, "ok", null]);
                    }
                    else
                    {
                        var reason = outcome.Error ?? "Unknown error.";
                        failures.Add(new BatchFailure(outcome.Index, name, reason));
                        manifest.Add([name + defaultExtension, request.Items[outcome.Index].Payload, "failed", reason]);
                    }

                    progress?.Report(new BatchProgress(completed + 1, total, name));
                }

                await WriteManifestAsync(archive, manifest, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await DrainAsync(inFlight).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            await DrainAsync(inFlight).ConfigureAwait(false);
            _logger.LogError(ex, "Writing the batch archive failed.");
            return Error.Unexpected($"Could not write the archive: {ex.Message}");
        }

        return new BatchResult(succeeded, failures);
    }

    internal static string ExtensionFor(ExportFormat format) => format switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.Jpeg => ".jpg",
        ExportFormat.Webp => ".webp",
        ExportFormat.Svg => ".svg",
        ExportFormat.Pdf => ".pdf",
        _ => ".bin"
    };

    private static string[] AssignFileNames(IReadOnlyList<BatchItem> items)
    {
        var used = BatchFileNames.NewNameSet();
        used.Add(Path.GetFileNameWithoutExtension(ManifestFileName));
        var names = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var name = BatchFileNames.Sanitize(items[i].FileName) ?? BatchFileNames.Generated(i + 1);
            names[i] = BatchFileNames.MakeUnique(name, used);
        }

        return names;
    }

    private async Task<ItemOutcome> RenderItemAsync(BatchRequest request, int index, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = request.Items[index].Payload;
        if (string.IsNullOrWhiteSpace(payload)) return new ItemOutcome(index, null, "The payload is empty.");

        try
        {
            var encoded = encoder.Encode(new BarcodeEncodeRequest(payload, request.Format, request.ErrorCorrection));
            if (encoded.IsFailure) return new ItemOutcome(index, null, encoded.Error.Message);

            var rendered = await renderer.RenderAsync(encoded.Value, request.Style, request.RenderOptions, cancellationToken).ConfigureAwait(false);
            return rendered.IsSuccess
                ? new ItemOutcome(index, rendered.Value, null)
                : new ItemOutcome(index, null, rendered.Error.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Batch item {Index} failed.", index);
            return new ItemOutcome(index, null, ex.Message);
        }
    }

    private static async Task WriteManifestAsync(ZipArchive archive, List<string?[]> rows, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(ManifestFileName, CompressionLevel.Fastest);
        var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var writer = new StreamWriter(stream, CsvFormatter.Utf8WithBom, bufferSize: 16 * 1024, leaveOpen: true);
            await using (writer.ConfigureAwait(false))
            {
                await CsvFormatter.WriteRowAsync(writer, ManifestHeader, cancellationToken).ConfigureAwait(false);
                foreach (var row in rows)
                    await CsvFormatter.WriteRowAsync(writer, row, cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DrainAsync(Queue<Task<ItemOutcome>> inFlight)
    {
        while (inFlight.TryDequeue(out var task))
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected while cancelling */ }
        }
    }

    private sealed record ItemOutcome(int Index, RenderedImage? Image, string? Error);
}
