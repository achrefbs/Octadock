using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Annotations;
using Octadock.Core.Geometry;
using Octadock.Core.Primitives;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class OctadockProjectSerializerTests : IDisposable
{
    private readonly string _dir;
    private readonly OctadockProjectSerializer _serializer = new();

    public OctadockProjectSerializerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "OctadockProjectTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort
        }
    }

    private static byte[] FakePng(byte marker) => [0x89, 0x50, 0x4E, 0x47, marker, 0x0D, 0x0A];

    private static AnnotationDocument BuildDocument()
    {
        var doc = new AnnotationDocument(new PixelSize(1800, 1200), Guid.NewGuid());

        doc.Add(new AnnotationObject
        {
            Id = Guid.NewGuid(),
            Type = AnnotationObjectType.Arrow,
            Frame = new AnnotationFrame(100, 100, 300, 80),
            Style = new AnnotationStyle
            {
                Stroke = RgbaColor.Parse("#0078D4"),
                Fill = null,
                LineWidth = 4,
                Opacity = 1,
                ArrowHead = ArrowHeadStyle.Triangle,
            },
            Payload = new AnnotationPayload
            {
                Points = [new PointD(100, 100), new PointD(400, 180)],
            },
            ZIndex = 10,
            Locked = false,
        });

        doc.Add(new AnnotationObject
        {
            Id = Guid.NewGuid(),
            Type = AnnotationObjectType.Text,
            Frame = new AnnotationFrame(50, 400, 500, 60),
            Style = new AnnotationStyle
            {
                Stroke = RgbaColor.Parse("#FF000080"),
                Fill = RgbaColor.White,
                FontFamily = "Arial",
                FontSize = 32,
                Bold = true,
                Italic = true,
                TextAlignment = TextAlignment.Center,
                Opacity = 0.9,
            },
            Payload = new AnnotationPayload { Text = "Hello, Octadock!" },
            ZIndex = 20,
            Locked = true,
        });

        doc.Add(new AnnotationObject
        {
            Id = Guid.NewGuid(),
            Type = AnnotationObjectType.Counter,
            Frame = new AnnotationFrame(600, 600, 40, 40),
            Payload = new AnnotationPayload { CounterValue = 3 },
            ZIndex = 5,
        });

        return doc;
    }

    [Fact]
    public async Task Save_creates_a_zip_with_expected_entries()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "project.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), FakePng(2));

        File.Exists(path).Should().BeTrue();

        using ZipArchive archive = ZipFile.OpenRead(path);
        archive.GetEntry(OctadockProjectSerializer.ManifestEntry).Should().NotBeNull();
        archive.GetEntry(OctadockProjectSerializer.OriginalEntry).Should().NotBeNull();
        archive.GetEntry(OctadockProjectSerializer.PreviewEntry).Should().NotBeNull();
        archive.GetEntry(OctadockProjectSerializer.ObjectsEntry).Should().NotBeNull();
    }

    [Fact]
    public async Task Save_omits_preview_when_none_supplied()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "no-preview.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), previewPng: null);

        using ZipArchive archive = ZipFile.OpenRead(path);
        archive.GetEntry(OctadockProjectSerializer.PreviewEntry).Should().BeNull();
    }

    [Fact]
    public async Task Save_then_Load_preserves_base_image_bytes()
    {
        AnnotationDocument doc = BuildDocument();
        byte[] png = FakePng(0x42);
        string path = Path.Combine(_dir, "img.octadock");

        await _serializer.SaveAsync(path, doc, png, null);
        ProjectLoadResult result = await _serializer.LoadAsync(path);

        result.BaseImagePng.Should().Equal(png);
    }

    [Fact]
    public async Task Save_then_Load_preserves_canvas_and_source_capture_id()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "canvas.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), null);
        ProjectLoadResult result = await _serializer.LoadAsync(path);

        result.Document.CanvasSize.Should().Be(new PixelSize(1800, 1200));
        result.Document.SourceCaptureId.Should().Be(doc.SourceCaptureId);
        result.Manifest.Format.Should().Be("octadock-project");
        result.Manifest.Canvas.Width.Should().Be(1800);
        result.Manifest.Canvas.Height.Should().Be(1200);
    }

    [Fact]
    public async Task Save_then_Load_round_trips_all_object_data()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "objects.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), null);
        ProjectLoadResult result = await _serializer.LoadAsync(path);

        result.Document.Objects.Should().HaveCount(doc.Objects.Count);
        result.Document.Objects.Should().BeEquivalentTo(
            doc.Objects,
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task RoundTrip_preserves_rgba_colors_including_alpha()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "colors.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), null);
        ProjectLoadResult result = await _serializer.LoadAsync(path);

        AnnotationObject text = result.Document.Objects.Single(o => o.Type == AnnotationObjectType.Text);
        text.Style.Stroke.Should().Be(RgbaColor.Parse("#FF000080"));
        text.Style.Fill.Should().Be(RgbaColor.White);
        text.Style.Bold.Should().BeTrue();
        text.Style.Italic.Should().BeTrue();
        text.Style.TextAlignment.Should().Be(TextAlignment.Center);
    }

    [Fact]
    public async Task RoundTrip_preserves_points_and_text_and_counter()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "payload.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), null);
        ProjectLoadResult result = await _serializer.LoadAsync(path);

        AnnotationObject arrow = result.Document.Objects.Single(o => o.Type == AnnotationObjectType.Arrow);
        arrow.Payload.Points.Should().Equal(new PointD(100, 100), new PointD(400, 180));

        AnnotationObject text = result.Document.Objects.Single(o => o.Type == AnnotationObjectType.Text);
        text.Payload.Text.Should().Be("Hello, Octadock!");
        text.Locked.Should().BeTrue();

        AnnotationObject counter = result.Document.Objects.Single(o => o.Type == AnnotationObjectType.Counter);
        counter.Payload.CounterValue.Should().Be(3);
    }

    [Fact]
    public async Task Objects_are_stored_as_json_with_hex_colors_and_string_enums()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "json.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), null);

        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = archive.GetEntry(OctadockProjectSerializer.ObjectsEntry)!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        string json = await reader.ReadToEndAsync();

        json.Should().Contain("#0078D4");          // hex color, not an RGBA object
        json.Should().Contain("\"arrow\"");         // enum serialized as camelCase string
        json.Should().Contain("\"triangle\"");      // ArrowHeadStyle as string
        json.Should().NotContain("\"r\":");         // no numeric channels
    }

    [Fact]
    public async Task Load_missing_file_throws()
    {
        Func<Task> act = () => _serializer.LoadAsync(Path.Combine(_dir, "does-not-exist.octadock"));
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Save_creates_missing_directories()
    {
        AnnotationDocument doc = BuildDocument();
        string nested = Path.Combine(_dir, "a", "b", "c", "deep.octadock");

        await _serializer.SaveAsync(nested, doc, FakePng(1), null);

        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public async Task Empty_document_round_trips_with_no_objects()
    {
        var doc = new AnnotationDocument(new PixelSize(640, 480));
        string path = Path.Combine(_dir, "empty.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), null);
        ProjectLoadResult result = await _serializer.LoadAsync(path);

        result.Document.Objects.Should().BeEmpty();
        result.Document.CanvasSize.Should().Be(new PixelSize(640, 480));
        result.Document.SourceCaptureId.Should().BeNull();
    }

    [Fact]
    public async Task Save_is_atomic_and_leaves_no_temp_files_behind()
    {
        // Regression for D-2: overwriting an existing package must not leave scratch
        // files, and must replace the prior content in place.
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "atomic.octadock");

        await _serializer.SaveAsync(path, doc, FakePng(1), null);
        await _serializer.SaveAsync(path, doc, FakePng(0x55), null); // overwrite

        ProjectLoadResult result = await _serializer.LoadAsync(path);
        result.BaseImagePng.Should().Equal(FakePng(0x55));
        Directory.GetFiles(_dir).Should().ContainSingle(); // no *.tmp-* siblings
    }

    [Fact]
    public async Task Save_cancellation_preserves_existing_package()
    {
        AnnotationDocument doc = BuildDocument();
        string path = Path.Combine(_dir, "cancel.octadock");
        await _serializer.SaveAsync(path, doc, FakePng(1), null);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Func<Task> act = () => _serializer.SaveAsync(path, doc, FakePng(2), null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Original content survives, and no temp file is orphaned.
        ProjectLoadResult result = await _serializer.LoadAsync(path);
        result.BaseImagePng.Should().Equal(FakePng(1));
        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task Load_tolerates_manifest_with_null_metadata_and_canvas()
    {
        // Regression for D-3: a hand-edited manifest with explicit null sections
        // must load cleanly rather than throwing NullReferenceException.
        string path = Path.Combine(_dir, "nullsections.octadock");
        using (var file = new FileStream(path, FileMode.Create))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            ZipArchiveEntry manifest = archive.CreateEntry(OctadockProjectSerializer.ManifestEntry);
            await using (var writer = new StreamWriter(manifest.Open()))
            {
                await writer.WriteAsync(
                    "{\"format\":\"octadock-project\",\"canvas\":null,\"metadata\":null}");
            }

            ZipArchiveEntry original = archive.CreateEntry(OctadockProjectSerializer.OriginalEntry);
            await using var img = original.Open();
            await img.WriteAsync(FakePng(1));
        }

        ProjectLoadResult result = await _serializer.LoadAsync(path);
        result.Document.CanvasSize.Should().Be(new PixelSize(0, 0));
        result.Document.SourceCaptureId.Should().BeNull();
    }
}
