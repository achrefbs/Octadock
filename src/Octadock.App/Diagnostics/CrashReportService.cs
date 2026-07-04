using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;

namespace Octadock.App.Diagnostics;

/// <summary>
/// Writes opt-in, local crash reports. No network transport is performed here;
/// the setting simply controls whether a diagnostic JSON file is saved.
/// </summary>
internal sealed class CrashReportService
{
    internal const string CrashReportsFolder = "CrashReports";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ISettingsService _settings;
    private readonly IStoragePaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<CrashReportService> _logger;

    public CrashReportService(
        ISettingsService settings,
        IStoragePaths paths,
        IClock clock,
        ILogger<CrashReportService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal string ReportsDirectory => Path.Combine(_paths.RootDirectory, CrashReportsFolder);

    public string? TryWrite(Exception exception, string source, bool isTerminating = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!_settings.Current.General.CrashReportingEnabled)
        {
            return null;
        }

        try
        {
            DateTimeOffset timestamp = _clock.UtcNow;
            CrashReport report = CreateReport(exception, source, isTerminating, timestamp);
            Directory.CreateDirectory(ReportsDirectory);

            string filePath = Path.Combine(
                ReportsDirectory,
                BuildFileName(timestamp, source));

            File.WriteAllText(filePath, JsonSerializer.Serialize(report, JsonOptions));
            _logger.LogInformation("Wrote local crash report to {Path}.", filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write local crash report.");
            return null;
        }
    }

    internal CrashReport CreateReport(
        Exception exception,
        string source,
        bool isTerminating,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string safeSource = string.IsNullOrWhiteSpace(source)
            ? "Unknown"
            : RedactSensitivePaths(source.Trim(), _paths.RootDirectory);

        return new CrashReport(
            Application: "Octadock",
            Version: GetApplicationVersion(),
            Source: safeSource,
            TimestampUtc: timestampUtc.ToUniversalTime(),
            IsTerminating: isTerminating,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            OperatingSystem: RuntimeInformation.OSDescription,
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessId: Environment.ProcessId,
            Exception: CreateExceptionReport(exception, depth: 0));
    }

    internal ExceptionReport CreateExceptionReport(Exception exception, int depth)
    {
        ArgumentNullException.ThrowIfNull(exception);

        IReadOnlyList<ExceptionReport> innerExceptions = depth >= 5
            ? Array.Empty<ExceptionReport>()
            : GetInnerExceptions(exception)
                .Select(inner => CreateExceptionReport(inner, depth + 1))
                .ToArray();

        return new ExceptionReport(
            Type: exception.GetType().FullName ?? exception.GetType().Name,
            Message: RedactSensitivePaths(exception.Message, _paths.RootDirectory),
            HResult: exception.HResult,
            TargetSite: RedactSensitivePaths(exception.TargetSite?.ToString() ?? string.Empty, _paths.RootDirectory),
            StackTrace: RedactSensitivePaths(exception.StackTrace ?? string.Empty, _paths.RootDirectory),
            InnerExceptions: innerExceptions);
    }

    internal static string BuildFileName(DateTimeOffset timestampUtc, string source)
    {
        string safeSource = SanitizeFileToken(source);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"octadock-crash-{timestampUtc.ToUniversalTime():yyyyMMdd-HHmmss-fffffff}-{safeSource}.json");
    }

    internal static string SanitizeFileToken(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = trimmed.Length <= 128
            ? stackalloc char[trimmed.Length]
            : new char[128];

        int written = 0;
        foreach (char ch in trimmed)
        {
            if (written >= buffer.Length)
            {
                break;
            }

            buffer[written++] = invalid.Contains(ch) || char.IsWhiteSpace(ch)
                ? '-'
                : char.ToLowerInvariant(ch);
        }

        string token = new(buffer[..written]);
        token = token.Trim('-');
        return token.Length == 0 ? "unknown" : token;
    }

    internal static string RedactSensitivePaths(string value, string rootDirectory)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        string redacted = value;
        foreach ((string Path, string Token) item in GetRedactions(rootDirectory)
                     .OrderByDescending(item => item.Path.Length))
        {
            redacted = redacted.Replace(item.Path, item.Token, StringComparison.OrdinalIgnoreCase);
            string alternate = item.Path.Replace('\\', '/');
            if (!string.Equals(alternate, item.Path, StringComparison.Ordinal))
            {
                redacted = redacted.Replace(alternate, item.Token, StringComparison.OrdinalIgnoreCase);
            }
        }

        return redacted;
    }

    private static IEnumerable<(string Path, string Token)> GetRedactions(string rootDirectory)
    {
        yield return (Path.GetFullPath(rootDirectory).TrimEnd('\\', '/'), "%OCTADOCK_DATA%");
        foreach ((Environment.SpecialFolder Folder, string Token) item in new[]
                 {
                     (Environment.SpecialFolder.UserProfile, "%USERPROFILE%"),
                     (Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
                     (Environment.SpecialFolder.ApplicationData, "%APPDATA%"),
                 })
        {
            string path = Environment.GetFolderPath(item.Folder);
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return (Path.GetFullPath(path).TrimEnd('\\', '/'), item.Token);
            }
        }
    }

    private static IReadOnlyList<Exception> GetInnerExceptions(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions;
        }

        return exception.InnerException is null
            ? Array.Empty<Exception>()
            : new[] { exception.InnerException };
    }

    private static string GetApplicationVersion()
    {
        Assembly assembly = typeof(CrashReportService).Assembly;
        return FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}

internal sealed record CrashReport(
    string Application,
    string Version,
    string Source,
    DateTimeOffset TimestampUtc,
    bool IsTerminating,
    string ProcessArchitecture,
    string OperatingSystem,
    string FrameworkDescription,
    int ProcessId,
    ExceptionReport Exception);

internal sealed record ExceptionReport(
    string Type,
    string Message,
    int HResult,
    string TargetSite,
    string StackTrace,
    IReadOnlyList<ExceptionReport> InnerExceptions);
