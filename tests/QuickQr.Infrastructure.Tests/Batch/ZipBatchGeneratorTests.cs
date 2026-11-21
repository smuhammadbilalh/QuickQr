using System.IO.Compression;
using System.Text;
using QuickQr.Core;
using QuickQr.Core.Barcodes;
using QuickQr.Core.Batch;
using QuickQr.Core.Licensing;
using QuickQr.Core.Rendering;
using QuickQr.Infrastructure.Batch;

namespace QuickQr.Infrastructure.Tests.Batch;

public sealed class ZipBatchGeneratorTests
{
    private readonly IBarcodeEncoder _encoder = Substitute.For<IBarcodeEncoder>();
    private readonly IBarcodeRenderer _renderer = Substitute.For<IBarcodeRenderer>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();

    public ZipBatchGeneratorTests()
    {
        _entitlements.HasFeature(PremiumFeature.BatchGeneration).Returns(true);
        _encoder.Encode(Arg.Any<BarcodeEncodeRequest>())
            .Returns(call => Result.Success(new BarcodeMatrix(21, 21, BarcodeFormat.QrCode) { Payload = call.Arg<BarcodeEncodeRequest>().Payload }));
        _renderer.RenderAsync(Arg.Any<BarcodeMatrix>(), Arg.Any<QrStyle>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Result.Success(new RenderedImage(Encoding.UTF8.GetBytes("img:" + call.Arg<BarcodeMatrix>().Payload), ExportFormat.Png, 64, 64))));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writes_one_entry_per_item_in_order_plus_manifest()
    {
        var items = Enumerable.Range(1, 12).Select(i => new BatchItem($"payload-{i}", $"code {i}")).ToList();
        var progress = new List<BatchProgress>();
        using var output = new MemoryStream();

        var result = await CreateGenerator().GenerateZipAsync(Request(items), output, new SyncProgress<BatchProgress>(progress.Add), Ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(12);
        result.Value.Failures.Should().BeEmpty();

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should().Equal(items.Select(i => i.FileName + ".png").Append("manifest.csv"));
        ReadEntry(zip, "code 7.png").Should().Be("img:payload-7");

        var manifest = ReadEntry(zip, "manifest.csv").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        manifest[0].Should().Be("﻿file,payload,status,error");
        manifest[1].Should().Be("code 1.png,payload-1,ok,");
        manifest.Should().HaveCount(13);

        progress[0].Should().Be(new BatchProgress(0, 12, null));
        progress[^1].Should().Be(new BatchProgress(12, 12, "code 12"));
        progress.Select(p => p.Completed).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Collects_failures_without_aborting_the_batch()
    {
        _encoder.Encode(Arg.Is<BarcodeEncodeRequest>(r => r.Payload == "bad-encode")).Returns(Result.Failure<BarcodeMatrix>(Error.Validation("Too long for a QR code.")));
        _renderer.RenderAsync(Arg.Is<BarcodeMatrix>(m => m.Payload == "bad-render"), Arg.Any<QrStyle>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<RenderedImage>(Error.Unexpected("Renderer exploded."))));
        _encoder.Encode(Arg.Is<BarcodeEncodeRequest>(r => r.Payload == "throws")).Returns(_ => throw new InvalidOperationException("Encoder crashed."));

        var items = new[]
        {
            new BatchItem("good-1", "a"), new BatchItem("bad-encode", "b"), new BatchItem("bad-render", "c"),
            new BatchItem("throws", "d"), new BatchItem("  ", "e"), new BatchItem("good-2", "f")
        };
        using var output = new MemoryStream();

        var result = await CreateGenerator().GenerateZipAsync(Request(items), output, cancellationToken: Ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().Be(2);
        result.Value.Failures.Should().Equal(
            new BatchFailure(1, "b", "Too long for a QR code."),
            new BatchFailure(2, "c", "Renderer exploded."),
            new BatchFailure(3, "d", "Encoder crashed."),
            new BatchFailure(4, "e", "The payload is empty."));

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should().Equal("a.png", "f.png", "manifest.csv");
        var manifest = ReadEntry(zip, "manifest.csv");
        manifest.Should().Contain("b.png,bad-encode,failed,Too long for a QR code.\r\n");
        manifest.Should().Contain("f.png,good-2,ok,\r\n");
    }

    [Fact]
    public async Task Sanitises_and_deduplicates_entry_names()
    {
        var items = new[] { new BatchItem("1", "same"), new BatchItem("2", "SAME"), new BatchItem("3", "a/b"), new BatchItem("4", ""), new BatchItem("5", "manifest") };
        using var output = new MemoryStream();

        await CreateGenerator().GenerateZipAsync(Request(items), output, cancellationToken: Ct);

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should().Equal("same.png", "SAME-2.png", "a_b.png", "code-0004.png", "manifest-2.png", "manifest.csv");
    }

    [Fact]
    public async Task Manifest_cells_are_injection_safe()
    {
        using var output = new MemoryStream();

        await CreateGenerator().GenerateZipAsync(Request([new BatchItem("=cmd|' /C calc'!A0", "x")]), output, cancellationToken: Ct);

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        ReadEntry(zip, "manifest.csv").Should().Contain("x.png,'=cmd|' /C calc'!A0,ok,");
    }

    [Fact]
    public async Task Is_locked_without_the_business_feature()
    {
        _entitlements.HasFeature(PremiumFeature.BatchGeneration).Returns(false);
        using var output = new MemoryStream();

        var result = await CreateGenerator().GenerateZipAsync(Request([new BatchItem("x", "x")]), output, cancellationToken: Ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.FeatureLocked);
        output.Length.Should().Be(0);
        _encoder.DidNotReceiveWithAnyArgs().Encode(default!);
    }

    [Fact]
    public async Task Rejects_an_empty_batch()
    {
        using var output = new MemoryStream();

        var result = await CreateGenerator().GenerateZipAsync(Request([]), output, cancellationToken: Ct);

        result.Error.Code.Should().Be(ErrorCodes.Validation);
    }

    [Fact]
    public async Task Cancellation_stops_the_batch_and_throws()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var rendered = 0;
        _renderer.RenderAsync(Arg.Any<BarcodeMatrix>(), Arg.Any<QrStyle>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref rendered) == 3) await cts.CancelAsync();
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                return Result.Success(new RenderedImage([1, 2, 3], ExportFormat.Png, 1, 1));
            });
        var items = Enumerable.Range(1, 50).Select(i => new BatchItem($"p{i}", $"n{i}")).ToList();
        using var output = new MemoryStream();

        var act = () => CreateGenerator(parallelism: 1).GenerateZipAsync(Request(items), output, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        rendered.Should().BeLessThan(50);
    }

    [Fact]
    public async Task Uses_the_rendered_file_extension()
    {
        _renderer.RenderAsync(Arg.Any<BarcodeMatrix>(), Arg.Any<QrStyle>(), Arg.Any<RenderOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RenderedImage("<svg/>"u8.ToArray(), ExportFormat.Svg, 1, 1))));
        using var output = new MemoryStream();

        await CreateGenerator().GenerateZipAsync(Request([new BatchItem("x", "vector")], ExportFormat.Svg), output, cancellationToken: Ct);

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        zip.Entries.Select(e => e.FullName).Should().Equal("vector.svg", "manifest.csv");
    }

    private ZipBatchGenerator CreateGenerator(int parallelism = 4) =>
        new(_encoder, _renderer, _entitlements) { MaxDegreeOfParallelism = parallelism };

    private static BatchRequest Request(IReadOnlyList<BatchItem> items, ExportFormat format = ExportFormat.Png) =>
        new(items, BarcodeFormat.QrCode, ErrorCorrectionLevel.Medium, QrStyle.Default, new RenderOptions(256, format));

    /// <summary>Decodes the raw entry bytes (keeps a BOM as U+FEFF, unlike StreamReader).</summary>
    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var entry = zip.GetEntry(name)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Synchronous IProgress (Progress&lt;T&gt; posts asynchronously).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
