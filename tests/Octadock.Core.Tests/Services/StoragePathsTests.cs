using FluentAssertions;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class StoragePathsTests : IDisposable
{
    private readonly string _root;
    private readonly StoragePaths _paths;

    public StoragePathsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OctadockTests", Guid.NewGuid().ToString("N"));
        _paths = new StoragePaths(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Default_root_is_under_local_application_data()
    {
        var defaults = new StoragePaths();
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Octadock");

        defaults.RootDirectory.Should().Be(expected);
    }

    [Fact]
    public void Subdirectories_hang_off_the_root()
    {
        _paths.CapturesDirectory.Should().Be(Path.Combine(_root, "Captures"));
        _paths.ProjectsDirectory.Should().Be(Path.Combine(_root, "Projects"));
        _paths.MockupsDirectory.Should().Be(Path.Combine(_root, "Mockups"));
        _paths.RecordingsDirectory.Should().Be(Path.Combine(_root, "Recordings"));
        _paths.ThumbnailsDirectory.Should().Be(Path.Combine(_root, "Thumbnails"));
        _paths.TempExportsDirectory.Should().Be(Path.Combine(_root, "TempExports"));
        _paths.LogsDirectory.Should().Be(Path.Combine(_root, "Logs"));
    }

    [Fact]
    public void DatabasePath_is_root_octadock_db()
    {
        _paths.DatabasePath.Should().Be(Path.Combine(_root, "octadock.db"));
    }

    [Fact]
    public void EnsureDirectories_creates_all_folders()
    {
        _paths.EnsureDirectories();

        Directory.Exists(_paths.RootDirectory).Should().BeTrue();
        Directory.Exists(_paths.CapturesDirectory).Should().BeTrue();
        Directory.Exists(_paths.ProjectsDirectory).Should().BeTrue();
        Directory.Exists(_paths.MockupsDirectory).Should().BeTrue();
        Directory.Exists(_paths.RecordingsDirectory).Should().BeTrue();
        Directory.Exists(_paths.ThumbnailsDirectory).Should().BeTrue();
        Directory.Exists(_paths.TempExportsDirectory).Should().BeTrue();
        Directory.Exists(_paths.LogsDirectory).Should().BeTrue();
    }

    [Fact]
    public void EnsureDirectories_is_idempotent()
    {
        _paths.EnsureDirectories();
        Action second = () => _paths.EnsureDirectories();
        second.Should().NotThrow();
    }

    [Fact]
    public void BuildCaptureRelativePath_uses_date_partitioned_layout()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var created = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

        string rel = _paths.BuildCaptureRelativePath(id, created, ".png");

        rel.Should().Be("Captures/2026/07/01/11111111-2222-3333-4444-555555555555.png");
    }

    [Fact]
    public void BuildCaptureRelativePath_normalizes_extension_without_dot()
    {
        var id = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 12, 5, 0, 0, 0, TimeSpan.Zero);

        string rel = _paths.BuildCaptureRelativePath(id, created, "jpg");
        rel.Should().EndWith(".jpg");
        rel.Should().Contain("2026/12/05");
    }

    [Fact]
    public void BuildThumbnailRelativePath_is_thumbnails_id_jpg()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        _paths.BuildThumbnailRelativePath(id)
            .Should().Be("Thumbnails/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg");
    }

    [Fact]
    public void BuildMockupRelativePath_links_capture_and_variant_in_date_partition()
    {
        var captureId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var variantId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var created = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);

        _paths.BuildMockupRelativePath(captureId, variantId, created).Should().Be(
            "Mockups/2026/07/10/11111111-2222-3333-4444-555555555555-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.png");
    }

    [Fact]
    public void BuildRecordingRelativePath_uses_recordings_folder()
    {
        var id = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero);

        string rel = _paths.BuildRecordingRelativePath(id, created, ".mp4");
        rel.Should().StartWith("Recordings/2026/03/09/");
        rel.Should().EndWith(".mp4");
    }

    [Fact]
    public void ToAbsolute_resolves_relative_with_forward_slashes()
    {
        string abs = _paths.ToAbsolute("Captures/2026/07/01/x.png");
        abs.Should().Be(Path.GetFullPath(Path.Combine(_root, "Captures", "2026", "07", "01", "x.png")));
    }

    [Fact]
    public void ToAbsolute_tolerates_backslashes()
    {
        string abs = _paths.ToAbsolute("Thumbnails\\x.jpg");
        abs.Should().Be(Path.GetFullPath(Path.Combine(_root, "Thumbnails", "x.jpg")));
    }

    [Fact]
    public void ToRelative_of_absolute_under_root_uses_forward_slashes()
    {
        string abs = Path.Combine(_root, "Captures", "2026", "07", "01", "x.png");
        string rel = _paths.ToRelative(abs);
        rel.Should().Be("Captures/2026/07/01/x.png");
    }

    [Fact]
    public void ToRelative_returns_input_when_outside_root()
    {
        string outside = Path.Combine(Path.GetTempPath(), "SomewhereElse", "file.png");
        _paths.ToRelative(outside).Should().Be(outside);
    }

    [Fact]
    public void ToAbsolute_then_ToRelative_round_trips()
    {
        const string rel = "Captures/2026/07/01/abc.png";
        string abs = _paths.ToAbsolute(rel);
        _paths.ToRelative(abs).Should().Be(rel);
    }

    [Fact]
    public void ToAbsolute_rejects_parent_directory_escape()
    {
        Action act = () => _paths.ToAbsolute("../outside.png");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("relativePath");
    }

    [Fact]
    public void ToAbsolute_rejects_absolute_path_outside_root()
    {
        string outside = Path.Combine(Path.GetTempPath(), "SomewhereElse", "file.png");
        Action act = () => _paths.ToAbsolute(outside);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("relativePath");
    }

    [Fact]
    public void ToAbsolute_accepts_absolute_path_under_root()
    {
        string under = Path.Combine(_root, "Captures", "x.png");
        _paths.ToAbsolute(under).Should().Be(Path.GetFullPath(under));
    }
}
