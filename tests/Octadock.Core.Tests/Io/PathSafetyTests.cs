using FluentAssertions;
using Octadock.Core.Io;
using Xunit;

namespace Octadock.Core.Tests.Io;

public class PathSafetyTests
{
    [Theory]
    [InlineData(@"\\host\share\file.png")]
    [InlineData(@"\\192.168.1.5\c$\x.txt")]
    [InlineData("//host/share/file.png")]
    public void IsUncPath_flags_network_paths(string path)
        => PathSafety.IsUncPath(path).Should().BeTrue();

    [Theory]
    [InlineData(@"C:\Users\me\pic.png")]
    [InlineData("relative/file.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void IsUncPath_allows_local_and_empty(string? path)
        => PathSafety.IsUncPath(path).Should().BeFalse();

    [Theory]
    [InlineData("setup.exe")]
    [InlineData(@"C:\x\run.BAT")]
    [InlineData("script.ps1")]
    [InlineData("shortcut.lnk")]
    [InlineData("installer.msi")]
    [InlineData("screensaver.scr")]
    [InlineData("payload.js")]
    public void IsExecutableExtension_flags_executables(string path)
        => PathSafety.IsExecutableExtension(path).Should().BeTrue();

    [Theory]
    [InlineData("photo.png")]
    [InlineData("notes.txt")]
    [InlineData("data.csv")]
    [InlineData("archive.zip")]
    [InlineData("noext")]
    public void IsExecutableExtension_allows_documents(string path)
        => PathSafety.IsExecutableExtension(path).Should().BeFalse();
}
