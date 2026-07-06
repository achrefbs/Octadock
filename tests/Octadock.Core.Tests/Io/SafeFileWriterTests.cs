using System.Text;
using FluentAssertions;
using Octadock.Core.Io;
using Xunit;

namespace Octadock.Core.Tests.Io;

/// <summary>
/// Proves the SafeFileWriter data-loss guarantee (WS9, R23): a write killed at any
/// point never corrupts the original, and every overwrite is restorable.
/// </summary>
public sealed class SafeFileWriterTests : IDisposable
{
    private readonly string _dir;
    private readonly SafeFileWriter _writer;

    public SafeFileWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "octadock-sfw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _writer = new SafeFileWriter(new FileRevisionStore(Path.Combine(_dir, "_revisions")));
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private bool HasStrayTempFiles()
        => Directory.GetFiles(_dir)
            .Select(Path.GetFileName)
            .Any(n => n!.StartsWith(".octadock-tmp-", StringComparison.Ordinal)
                   || n.StartsWith(".octadock-bak-", StringComparison.Ordinal));

    [Fact]
    public async Task WriteAsync_creates_a_new_file()
    {
        string p = PathFor("new.bin");
        await _writer.WriteAsync(p, new byte[] { 1, 2, 3 });
        File.ReadAllBytes(p).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Overwrite_keeps_prior_content_as_a_restorable_revision()
    {
        string p = PathFor("doc.txt");
        await File.WriteAllTextAsync(p, "ORIGINAL");

        await _writer.WriteAsync(p, Encoding.UTF8.GetBytes("UPDATED"));
        File.ReadAllText(p).Should().Be("UPDATED");

        (await _writer.RestoreLatestAsync(p)).Should().BeTrue();
        File.ReadAllText(p).Should().Be("ORIGINAL", "the prior version must be restorable");
    }

    [Fact]
    public async Task Failure_mid_write_leaves_the_original_intact_and_no_temp_files()
    {
        string p = PathFor("safe.txt");
        await File.WriteAllTextAsync(p, "SAFE");

        Func<Task> act = () => _writer.WriteAsync(p, async (stream, ct) =>
        {
            await stream.WriteAsync(new byte[] { 9, 9, 9 }, ct);
            throw new InvalidOperationException("simulated crash mid-write");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllText(p).Should().Be("SAFE", "a failed write must not touch the original");
        HasStrayTempFiles().Should().BeFalse("temp/backup scratch files are cleaned up");
    }

    [Fact]
    public async Task Killing_the_write_100_times_never_corrupts_the_original()
    {
        string p = PathFor("hot.dat");
        byte[] original = Encoding.UTF8.GetBytes("THE-ORIGINAL-CONTENT-v0");
        await File.WriteAllBytesAsync(p, original);

        for (int i = 0; i < 100; i++)
        {
            int throwAfter = i % 7; // vary how many bytes land before the "crash"
            try
            {
                await _writer.WriteAsync(p, async (stream, ct) =>
                {
                    for (int b = 0; b < throwAfter; b++)
                    {
                        await stream.WriteAsync(new byte[] { (byte)b }, ct);
                    }

                    throw new IOException($"simulated kill #{i}");
                });
            }
            catch (IOException)
            {
                // Expected: the process "died" mid-write.
            }

            File.ReadAllBytes(p).Should().Equal(original, "iteration {0}: original must stay byte-identical", i);
        }

        HasStrayTempFiles().Should().BeFalse();
    }

    [Fact]
    public async Task CopyAsync_copies_content_atomically()
    {
        string src = PathFor("src.bin");
        string dst = PathFor("dst.bin");
        await File.WriteAllBytesAsync(src, new byte[] { 5, 6, 7, 8 });

        await _writer.CopyAsync(src, dst);
        File.ReadAllBytes(dst).Should().Equal(5, 6, 7, 8);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
