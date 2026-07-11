using FluentAssertions;
using Octadock.App.Services;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class CaptureCoordinatorFileIdentityTests
{
    [Fact]
    public async Task FilesHaveSameContentAsync_detects_copied_capture_bytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "octadock-file-identity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "first.png");
            string copy = Path.Combine(root, "copy.png");
            string different = Path.Combine(root, "different.png");
            byte[] bytes = [1, 4, 9, 16, 25, 36];
            await File.WriteAllBytesAsync(first, bytes);
            await File.WriteAllBytesAsync(copy, bytes);
            await File.WriteAllBytesAsync(different, [1, 4, 9, 16, 25, 37]);

            (await CaptureCoordinator.FilesHaveSameContentAsync(first, copy)).Should().BeTrue();
            (await CaptureCoordinator.FilesHaveSameContentAsync(first, different)).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
