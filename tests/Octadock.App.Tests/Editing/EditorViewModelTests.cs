using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Editing;
using Octadock.Core.Annotations;
using Octadock.Core.Geometry;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Octadock.App.Tests.Editing;

public sealed class EditorViewModelTests
{
    [Fact]
    public async Task SaveCommand_keeps_dirty_when_host_save_returns_false()
    {
        var host = new FakeEditorHost { SaveResult = false };
        EditorViewModel viewModel = CreateDirtyViewModel(host);

        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.IsDirty.Should().BeTrue();
        host.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task SaveCommand_clears_dirty_when_host_save_succeeds()
    {
        var host = new FakeEditorHost { SaveResult = true };
        EditorViewModel viewModel = CreateDirtyViewModel(host);

        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.IsDirty.Should().BeFalse();
        host.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsCommand_keeps_dirty_when_host_save_as_returns_false()
    {
        var host = new FakeEditorHost { SaveAsResult = false };
        EditorViewModel viewModel = CreateDirtyViewModel(host);

        await viewModel.SaveAsCommand.ExecuteAsync(null);

        viewModel.IsDirty.Should().BeTrue();
        host.SaveAsCalls.Should().Be(1);
    }

    private static EditorViewModel CreateDirtyViewModel(EditorHost host)
    {
        var document = new AnnotationDocument(new PixelSize(20, 20));
        var viewModel = new EditorViewModel(document, TestBitmap.Create(20, 20), NullLogger.Instance)
        {
            Host = host,
        };

        viewModel.AddObject(AnnotationObject.Create(
            AnnotationObjectType.Rectangle,
            new AnnotationFrame(1, 2, 10, 12),
            zIndex: 0));
        viewModel.IsDirty.Should().BeTrue();
        return viewModel;
    }

    private sealed class FakeEditorHost : EditorHost
    {
        public bool SaveResult { get; init; }

        public bool SaveAsResult { get; init; }

        public int SaveCalls { get; private set; }

        public int SaveAsCalls { get; private set; }

        public override Task CopyAsync() => Task.CompletedTask;

        public override Task<bool> SaveAsync()
        {
            SaveCalls++;
            return Task.FromResult(SaveResult);
        }

        public override Task<bool> SaveAsAsync()
        {
            SaveAsCalls++;
            return Task.FromResult(SaveAsResult);
        }

        public override Task ExportAsync() => Task.CompletedTask;

        public override void BeginTextEditing(AnnotationObject textObject)
        {
        }
    }

    private static class TestBitmap
    {
        public static BitmapSource Create(int width, int height)
        {
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            BitmapSource bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                palette: null,
                pixels,
                stride);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
