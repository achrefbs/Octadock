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
    [InlineData("website.url")]
    [InlineData(@"C:\Downloads\WEBSITE.URL")]
    [InlineData("website.url ")]
    [InlineData("website.url.")]
    public void IsExecutableExtension_flags_internet_shortcuts_case_insensitively(string path)
        => PathSafety.IsExecutableExtension(path).Should().BeTrue();

    [Theory]
    [InlineData("legacy.SCF")]
    [InlineData("setup.Application")]
    [InlineData("installed.APPREF-MS")]
    [InlineData("browser-app.XbAp")]
    [InlineData("favorite.WebSite")]
    [InlineData("query.SEARCH-MS")]
    [InlineData("connector.SearchConnector-MS")]
    [InlineData("workspace.LIBRARY-MS")]
    [InlineData("settings.SettingContent-MS")]
    [InlineData("support.DIAGCAB")]
    [InlineData("console.MsC")]
    [InlineData("help.ChM")]
    [InlineData("package.AppX")]
    [InlineData("package.AppXBundle")]
    [InlineData("package.MsIx")]
    [InlineData("package.MsIxBuNdLe")]
    [InlineData("remote.AppInstaller")]
    [InlineData("desktop.Theme")]
    [InlineData("desktop.ThemePack")]
    [InlineData("desktop.DeskThemePack")]
    public void IsExecutableExtension_flags_direct_shell_launch_formats_case_insensitively(string path)
        => PathSafety.IsExecutableExtension(path).Should().BeTrue();

    [Theory]
    [InlineData("photo.png")]
    [InlineData("notes.txt")]
    [InlineData("data.csv")]
    [InlineData("archive.zip")]
    [InlineData("internet-shortcut.url.txt")]
    [InlineData("deployment.application.txt")]
    [InlineData("shortcut.appref-ms.json")]
    [InlineData("noext")]
    public void IsExecutableExtension_allows_documents(string path)
        => PathSafety.IsExecutableExtension(path).Should().BeFalse();
}
