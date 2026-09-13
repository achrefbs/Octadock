using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Hotkeys;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="ISettingsService"/>. Projects the flat key/value rows of an
/// <see cref="ISettingsStore"/> onto the typed <see cref="OctadockSettings"/>
/// aggregate and back, applying defaults for any missing keys and parsing enums,
/// booleans, bounded integers and hotkey gestures robustly. Keeps a thread-safe
/// in-memory snapshot in <see cref="Current"/> and serializes writes so
/// functional updates cannot overwrite each other with stale snapshots.
/// </summary>
public sealed partial class SettingsService : ISettingsService, IDisposable
{
    private readonly ISettingsStore _store;
    private readonly ILogger<SettingsService> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private OctadockSettings _current = OctadockSettings.Defaults;

    /// <summary>Creates the settings service over a key/value store.</summary>
    public SettingsService(ISettingsStore store, ILogger<SettingsService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger<SettingsService>.Instance;
    }

    /// <inheritdoc />
    public OctadockSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyDictionary<string, string> all = await _store.GetAllAsync(cancellationToken).ConfigureAwait(false);
            OctadockSettings settings = FromDictionary(all);

            lock (_gate)
            {
                _current = settings;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IReadOnlyDictionary<string, string> flat = ToDictionary(settings);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.SetManyAsync(flat, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _current = settings;
            }

            RaiseChanged(settings);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        Func<OctadockSettings, OctadockSettings> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        OctadockSettings updated;

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OctadockSettings snapshot;
            lock (_gate)
            {
                snapshot = _current;
            }

            updated = mutate(snapshot)
                ?? throw new InvalidOperationException("The settings mutation returned null.");

            IReadOnlyDictionary<string, string> flat = ToDictionary(updated);
            await _store.SetManyAsync(flat, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _current = updated;
            }

            RaiseChanged(updated);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void RaiseChanged(OctadockSettings settings)
    {
        EventHandler<SettingsChangedEventArgs>? handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        var args = new SettingsChangedEventArgs(settings);
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<SettingsChangedEventArgs>)handler)(this, args);
            }
            catch (Exception ex)
            {
                LogSettingsChangedHandlerThrew(ex);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _saveGate.Dispose();

    /// <summary>Serializes the typed settings into the flat wire dictionary.</summary>
    internal static IReadOnlyDictionary<string, string> ToDictionary(OctadockSettings s)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SettingKeys.SettingsVersion] = s.Version.ToString(CultureInfo.InvariantCulture),

            // General
            [SettingKeys.GeneralLaunchAtLogin] = Bool(s.General.LaunchAtLogin),
            [SettingKeys.GeneralShowTrayIcon] = Bool(s.General.ShowTrayIcon),
            [SettingKeys.GeneralShowTaskbarIcon] = Bool(s.General.ShowTaskbarIcon),
            [SettingKeys.GeneralTheme] = s.General.Theme.ToString(),
            [SettingKeys.GeneralFirstRunCompleted] = Bool(s.General.FirstRunCompleted),
            [SettingKeys.GeneralCrashReportingEnabled] = Bool(s.General.CrashReportingEnabled),

            // Dock
            [SettingKeys.DockEnabled] = Bool(s.Dock.Enabled),
            [SettingKeys.DockHasCustomAnchor] = Bool(s.Dock.HasCustomAnchor),
            [SettingKeys.DockAnchorX] = Int(s.Dock.AnchorX),
            [SettingKeys.DockAnchorY] = Int(s.Dock.AnchorY),
            [SettingKeys.DockMonitorAnchors] = JsonSerializer.Serialize(s.Dock.MonitorAnchors),

            // Capture
            [SettingKeys.CaptureDefaultAction] = s.Capture.DefaultAction.ToString(),
            [SettingKeys.CaptureSaveDirectory] = s.Capture.SaveDirectory,
            [SettingKeys.CaptureFilenameTemplate] = s.Capture.FilenameTemplate,
            [SettingKeys.CaptureIncludeCursor] = Bool(s.Capture.IncludeCursor),
            [SettingKeys.CaptureWindowShadow] = Bool(s.Capture.WindowShadow),
            [SettingKeys.CaptureExcludeOctadockWindows] = Bool(s.Capture.ExcludeOctadockWindows),
            [SettingKeys.CaptureMultiMonitorMode] = s.Capture.MultiMonitorMode.ToString(),
            [SettingKeys.CaptureImageFormat] = s.Capture.ImageFormat.ToString(),
            [SettingKeys.CaptureJpegQuality] = Int(s.Capture.JpegQuality),
            [SettingKeys.CaptureSelfTimerSeconds] = Int(s.Capture.SelfTimerSeconds),
            [SettingKeys.CaptureFreezeScreen] = Bool(s.Capture.FreezeScreen),
            [SettingKeys.CapturePrecisionAids] = Bool(s.Capture.PrecisionAids),
            [SettingKeys.CaptureFixedSizeEnabled] = Bool(s.Capture.FixedSizeEnabled),
            [SettingKeys.CaptureFixedWidth] = Int(s.Capture.FixedWidth),
            [SettingKeys.CaptureFixedHeight] = Int(s.Capture.FixedHeight),
            [SettingKeys.CaptureLockAspectRatio] = Bool(s.Capture.LockAspectRatio),
            [SettingKeys.CaptureImageEditSaveBehavior] = s.Capture.ImageEditSaveBehavior.ToString(),

            // Shelf
            [SettingKeys.ShelfShowChrome] = Bool(s.Shelf.ShowChrome),
            [SettingKeys.ShelfAnchor] = s.Shelf.Anchor.ToString(),
            [SettingKeys.ShelfSize] = s.Shelf.Size.ToString(),
            [SettingKeys.ShelfAutoClose] = s.Shelf.AutoClose.ToString(),
            [SettingKeys.ShelfPeekBehavior] = s.Shelf.PeekBehavior.ToString(),
            [SettingKeys.ShelfRestoreEnabled] = Bool(s.Shelf.RestoreEnabled),
            [SettingKeys.ShelfMarginDip] = Int(s.Shelf.MarginDip),
            [SettingKeys.ShelfMaxItems] = Int(s.Shelf.MaxItems),

            // History
            [SettingKeys.HistoryEnabled] = Bool(s.History.Enabled),
            [SettingKeys.HistoryRetention] = s.History.Retention.ToString(),

            // Clipboard history
            [SettingKeys.ClipboardMonitorEnabled] = Bool(s.Clipboard.MonitorEnabled),
            [SettingKeys.ClipboardIncludeImages] = Bool(s.Clipboard.IncludeImages),
            [SettingKeys.ClipboardMaxItems] = Int(s.Clipboard.MaxItems),

            // OCR
            [SettingKeys.OcrProvider] = s.Ocr.Provider.ToString(),
            [SettingKeys.OcrOutputMode] = s.Ocr.OutputMode.ToString(),
            [SettingKeys.OcrPreferredLanguage] = s.Ocr.PreferredLanguage,

            // Speech
            [SettingKeys.SpeechProvider] = LocalSpeechProvider(s.Speech.Provider),
            [SettingKeys.SpeechParakeetModel] = s.Speech.ParakeetModel,
            [SettingKeys.SpeechWhisperModel] = s.Speech.WhisperModel,
            [SettingKeys.SpeechLanguage] = s.Speech.Language,
            [SettingKeys.SpeechMicrophoneDeviceId] = s.Speech.MicrophoneDeviceId,
            [SettingKeys.SpeechInsertionMode] = s.Speech.InsertionMode,
            [SettingKeys.SpeechCustomDictionary] = s.Speech.CustomDictionary,
            [SettingKeys.SpeechActivationMode] = s.Speech.ActivationMode,
            [SettingKeys.SpeechLivePartials] = Bool(s.Speech.LivePartials),
            [SettingKeys.SpeechAutoStopOnSilence] = Bool(s.Speech.AutoStopOnSilence),

            // Recording
            [SettingKeys.RecordingFps] = Int(s.Recording.Fps),
            [SettingKeys.RecordingQuality] = s.Recording.Quality.ToString(),
            [SettingKeys.RecordingIncludeCursor] = Bool(s.Recording.IncludeCursor),
            [SettingKeys.RecordingIncludeMicrophone] = Bool(s.Recording.IncludeMicrophone),
            [SettingKeys.RecordingIncludeSystemAudio] = Bool(s.Recording.IncludeSystemAudio),

            // Shortcuts
            [SettingKeys.ShortcutCaptureArea] = s.Shortcuts.CaptureArea.ToString(),
            [SettingKeys.ShortcutCaptureWindow] = s.Shortcuts.CaptureWindow.ToString(),
            [SettingKeys.ShortcutCaptureFullscreen] = s.Shortcuts.CaptureFullscreen.ToString(),
            [SettingKeys.ShortcutCapturePreviousArea] = s.Shortcuts.CapturePreviousArea.ToString(),
            // shortcuts.allInOne is no longer written (HUD removed); legacy rows stay inert.
            [SettingKeys.ShortcutDictation] = s.Shortcuts.Dictation.ToString(),
            [SettingKeys.ShortcutOcr] = s.Shortcuts.Ocr.ToString(),
            [SettingKeys.ShortcutRecord] = s.Shortcuts.Record.ToString(),
            [SettingKeys.ShortcutClipboardHistory] = s.Shortcuts.ClipboardHistory.ToString(),
            [SettingKeys.ShortcutReadAloud] = s.Shortcuts.ReadAloud.ToString(),

            // Read aloud
            [SettingKeys.ReadTtsProvider] = s.Read.TtsProvider,
            [SettingKeys.ReadVoice] = s.Read.Voice,
            [SettingKeys.ReadRate] = s.Read.Rate.ToString("0.###", CultureInfo.InvariantCulture),

            // Automation
            [SettingKeys.AutomationProtocolEnabled] = Bool(s.Automation.ProtocolEnabled),
            [SettingKeys.AutomationCliEnabled] = Bool(s.Automation.CliEnabled),
        };

        return d;
    }

    /// <summary>Builds a typed settings object from the flat store, filling defaults for missing keys.</summary>
    internal OctadockSettings FromDictionary(IReadOnlyDictionary<string, string> raw)
    {
        OctadockSettings d = OctadockSettings.Defaults;
        int loadedVersionFallback = HasPersistedSpeechSettings(raw) ? 1 : d.Version;
        int loadedVersion = GetInt(raw, SettingKeys.SettingsVersion, loadedVersionFallback);

        return d with
        {
            Version = Math.Max(loadedVersion, d.Version),
            General = new GeneralSettings
            {
                LaunchAtLogin = GetBool(raw, SettingKeys.GeneralLaunchAtLogin, d.General.LaunchAtLogin),
                ShowTrayIcon = GetBool(raw, SettingKeys.GeneralShowTrayIcon, d.General.ShowTrayIcon),
                ShowTaskbarIcon = GetBool(raw, SettingKeys.GeneralShowTaskbarIcon, d.General.ShowTaskbarIcon),
                Theme = GetThemeWithMigration(raw, d.General.Theme, loadedVersion),
                FirstRunCompleted = GetBool(raw, SettingKeys.GeneralFirstRunCompleted, d.General.FirstRunCompleted),
                CrashReportingEnabled = GetBool(raw, SettingKeys.GeneralCrashReportingEnabled, d.General.CrashReportingEnabled),
            },
            Dock = new DockSettings
            {
                Enabled = GetBool(raw, SettingKeys.DockEnabled, d.Dock.Enabled),
                HasCustomAnchor = GetBool(raw, SettingKeys.DockHasCustomAnchor, d.Dock.HasCustomAnchor),
                AnchorX = GetInt(raw, SettingKeys.DockAnchorX, d.Dock.AnchorX),
                AnchorY = GetInt(raw, SettingKeys.DockAnchorY, d.Dock.AnchorY),
                MonitorAnchors = ReadDockAnchors(raw),
            },
            Capture = new CaptureSettings
            {
                DefaultAction = GetEnum(raw, SettingKeys.CaptureDefaultAction, d.Capture.DefaultAction),
                SaveDirectory = GetString(raw, SettingKeys.CaptureSaveDirectory, d.Capture.SaveDirectory),
                FilenameTemplate = GetRequiredString(raw, SettingKeys.CaptureFilenameTemplate, d.Capture.FilenameTemplate),
                IncludeCursor = GetBool(raw, SettingKeys.CaptureIncludeCursor, d.Capture.IncludeCursor),
                WindowShadow = GetBool(raw, SettingKeys.CaptureWindowShadow, d.Capture.WindowShadow),
                ExcludeOctadockWindows = GetBool(raw, SettingKeys.CaptureExcludeOctadockWindows, d.Capture.ExcludeOctadockWindows),
                MultiMonitorMode = GetEnum(raw, SettingKeys.CaptureMultiMonitorMode, d.Capture.MultiMonitorMode),
                ImageFormat = GetEnum(raw, SettingKeys.CaptureImageFormat, d.Capture.ImageFormat),
                JpegQuality = GetInt(raw, SettingKeys.CaptureJpegQuality, d.Capture.JpegQuality, min: 1, max: 100),
                SelfTimerSeconds = GetInt(raw, SettingKeys.CaptureSelfTimerSeconds, d.Capture.SelfTimerSeconds, min: 0, max: 60),
                FreezeScreen = GetBool(raw, SettingKeys.CaptureFreezeScreen, d.Capture.FreezeScreen),
                PrecisionAids = GetBool(raw, SettingKeys.CapturePrecisionAids, d.Capture.PrecisionAids),
                FixedSizeEnabled = GetBool(raw, SettingKeys.CaptureFixedSizeEnabled, d.Capture.FixedSizeEnabled),
                FixedWidth = GetInt(raw, SettingKeys.CaptureFixedWidth, d.Capture.FixedWidth, min: 0, max: 32767),
                FixedHeight = GetInt(raw, SettingKeys.CaptureFixedHeight, d.Capture.FixedHeight, min: 0, max: 32767),
                LockAspectRatio = GetBool(raw, SettingKeys.CaptureLockAspectRatio, d.Capture.LockAspectRatio),
                ImageEditSaveBehavior = GetEnum(
                    raw,
                    SettingKeys.CaptureImageEditSaveBehavior,
                    d.Capture.ImageEditSaveBehavior),
            },
            Shelf = new ShelfSettings
            {
                ShowChrome = GetBool(raw, SettingKeys.ShelfShowChrome, d.Shelf.ShowChrome),
                Anchor = GetEnum(raw, SettingKeys.ShelfAnchor, d.Shelf.Anchor),
                Size = GetEnum(raw, SettingKeys.ShelfSize, d.Shelf.Size),
                AutoClose = GetShelfAutoClose(raw, d.Shelf.AutoClose),
                PeekBehavior = GetEnum(raw, SettingKeys.ShelfPeekBehavior, d.Shelf.PeekBehavior),
                RestoreEnabled = GetBool(raw, SettingKeys.ShelfRestoreEnabled, d.Shelf.RestoreEnabled),
                MarginDip = GetInt(raw, SettingKeys.ShelfMarginDip, d.Shelf.MarginDip, min: 0, max: 200),
                MaxItems = GetInt(raw, SettingKeys.ShelfMaxItems, d.Shelf.MaxItems, min: 1, max: 32),
            },
            History = new HistorySettings
            {
                Enabled = GetBool(raw, SettingKeys.HistoryEnabled, d.History.Enabled),
                Retention = GetHistoryRetention(raw, d.History.Retention),
            },
            Clipboard = new ClipboardSettings
            {
                MonitorEnabled = GetBool(raw, SettingKeys.ClipboardMonitorEnabled, d.Clipboard.MonitorEnabled),
                IncludeImages = GetBool(raw, SettingKeys.ClipboardIncludeImages, d.Clipboard.IncludeImages),
                MaxItems = GetInt(raw, SettingKeys.ClipboardMaxItems, d.Clipboard.MaxItems, min: 20, max: 5000),
            },
            Ocr = new OcrSettings
            {
                Provider = GetEnum(raw, SettingKeys.OcrProvider, d.Ocr.Provider),
                OutputMode = GetEnum(raw, SettingKeys.OcrOutputMode, d.Ocr.OutputMode),
                PreferredLanguage = GetString(raw, SettingKeys.OcrPreferredLanguage, d.Ocr.PreferredLanguage),
            },
            Speech = new SpeechSettings
            {
                Provider = LocalSpeechProvider(GetSpeechProviderWithMigration(raw, d.Speech.Provider, loadedVersion)),
                ParakeetModel = GetRequiredString(raw, SettingKeys.SpeechParakeetModel, d.Speech.ParakeetModel),
                WhisperModel = GetSpeechWhisperModel(raw, d.Speech.WhisperModel, loadedVersion),
                Language = GetSpeechLanguage(raw, d.Speech.Language, loadedVersion),
                MicrophoneDeviceId = GetString(raw, SettingKeys.SpeechMicrophoneDeviceId, d.Speech.MicrophoneDeviceId),
                InsertionMode = GetRequiredString(raw, SettingKeys.SpeechInsertionMode, d.Speech.InsertionMode),
                CustomDictionary = GetString(raw, SettingKeys.SpeechCustomDictionary, d.Speech.CustomDictionary),
                ActivationMode = GetRequiredString(raw, SettingKeys.SpeechActivationMode, d.Speech.ActivationMode),
                LivePartials = GetBool(raw, SettingKeys.SpeechLivePartials, d.Speech.LivePartials),
                AutoStopOnSilence = GetBool(raw, SettingKeys.SpeechAutoStopOnSilence, d.Speech.AutoStopOnSilence),
            },
            Recording = new RecordingSettings
            {
                Fps = GetInt(raw, SettingKeys.RecordingFps, d.Recording.Fps, min: 10, max: 60),
                Quality = GetEnum(raw, SettingKeys.RecordingQuality, d.Recording.Quality),
                IncludeCursor = GetBool(raw, SettingKeys.RecordingIncludeCursor, d.Recording.IncludeCursor),
                IncludeMicrophone = GetBool(raw, SettingKeys.RecordingIncludeMicrophone, d.Recording.IncludeMicrophone),
                IncludeSystemAudio = GetBool(raw, SettingKeys.RecordingIncludeSystemAudio, d.Recording.IncludeSystemAudio),
            },
            Shortcuts = new ShortcutSettings
            {
                CaptureArea = GetHotkey(raw, SettingKeys.ShortcutCaptureArea, d.Shortcuts.CaptureArea),
                CaptureWindow = GetHotkey(raw, SettingKeys.ShortcutCaptureWindow, d.Shortcuts.CaptureWindow),
                CaptureFullscreen = GetHotkey(raw, SettingKeys.ShortcutCaptureFullscreen, d.Shortcuts.CaptureFullscreen),
                CapturePreviousArea = GetHotkey(raw, SettingKeys.ShortcutCapturePreviousArea, d.Shortcuts.CapturePreviousArea),
                // The AllInOne shortcut is gone with the HUD; a stored shortcuts.allInOne value is ignored.
                Dictation = GetHotkey(raw, SettingKeys.ShortcutDictation, d.Shortcuts.Dictation),
                Ocr = GetHotkey(raw, SettingKeys.ShortcutOcr, d.Shortcuts.Ocr),
                Record = GetHotkey(raw, SettingKeys.ShortcutRecord, d.Shortcuts.Record),
                ClipboardHistory = GetHotkey(raw, SettingKeys.ShortcutClipboardHistory, d.Shortcuts.ClipboardHistory),
                ReadAloud = GetHotkey(raw, SettingKeys.ShortcutReadAloud, d.Shortcuts.ReadAloud),
            },
            Read = new ReadSettings
            {
                TtsProvider = ReadSettings.WindowsTtsProvider,
                Voice = GetString(raw, SettingKeys.ReadVoice, d.Read.Voice),
                Rate = GetDouble(raw, SettingKeys.ReadRate, d.Read.Rate, min: 0.5, max: 3.0),
            },
            Automation = new AutomationSettings
            {
                ProtocolEnabled = GetBool(raw, SettingKeys.AutomationProtocolEnabled, d.Automation.ProtocolEnabled),
                CliEnabled = GetBool(raw, SettingKeys.AutomationCliEnabled, d.Automation.CliEnabled),
            },
        };
    }

    // ---- Parsing helpers ------------------------------------------------

    private static string LocalSpeechProvider(string provider)
        => string.Equals(provider, SpeechSettings.WhisperProvider, StringComparison.OrdinalIgnoreCase)
            ? SpeechSettings.WhisperProvider : SpeechSettings.ParakeetProvider;

    private static Dictionary<string, DockAnchor> ReadDockAnchors(IReadOnlyDictionary<string, string> raw)
    {
        try
        {
            if (raw.TryGetValue(SettingKeys.DockMonitorAnchors, out string? json))
            {
                return JsonSerializer.Deserialize<Dictionary<string, DockAnchor>>(json)
                    ?? new Dictionary<string, DockAnchor>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException) { }
        return new Dictionary<string, DockAnchor>(StringComparer.OrdinalIgnoreCase);
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string GetString(IReadOnlyDictionary<string, string> raw, string key, string fallback)
        => raw.TryGetValue(key, out string? v) && v is not null ? v : fallback;

    private static string GetRequiredString(IReadOnlyDictionary<string, string> raw, string key, string fallback)
        => raw.TryGetValue(key, out string? v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> raw, string key, bool fallback)
    {
        if (!raw.TryGetValue(key, out string? v) || v is null)
        {
            return fallback;
        }

        return v.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => fallback,
        };
    }

    private static int GetInt(IReadOnlyDictionary<string, string> raw, string key, int fallback)
        => raw.TryGetValue(key, out string? v) &&
           int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> raw, string key, int fallback, int min, int max)
        => Math.Clamp(GetInt(raw, key, fallback), min, max);

    private static double GetDouble(
        IReadOnlyDictionary<string, string> raw, string key, double fallback, double min, double max)
        => raw.TryGetValue(key, out string? v) &&
           double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            ? Math.Clamp(n, min, max)
            : fallback;

    private TEnum GetEnum<TEnum>(IReadOnlyDictionary<string, string> raw, string key, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!raw.TryGetValue(key, out string? v) || string.IsNullOrWhiteSpace(v))
        {
            return fallback;
        }

        if (Enum.TryParse(v.Trim(), ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        LogUnrecognizedSetting(v, key);
        return fallback;
    }

    private ShelfAutoCloseMode GetShelfAutoClose(
        IReadOnlyDictionary<string, string> raw,
        ShelfAutoCloseMode fallback)
    {
        if (raw.TryGetValue(SettingKeys.ShelfAutoClose, out string? canonical) &&
            !string.IsNullOrWhiteSpace(canonical))
        {
            return GetEnum(raw, SettingKeys.ShelfAutoClose, fallback);
        }

        if (!raw.TryGetValue(SettingKeys.ShelfAutoCloseSecondsLegacy, out string? legacy) ||
            string.IsNullOrWhiteSpace(legacy))
        {
            return fallback;
        }

        if (!int.TryParse(legacy, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
        {
            LogUnrecognizedSetting(legacy, SettingKeys.ShelfAutoCloseSecondsLegacy);
            return fallback;
        }

        return seconds switch
        {
            0 => ShelfAutoCloseMode.Never,
            30 => ShelfAutoCloseMode.Seconds30,
            60 => ShelfAutoCloseMode.Minutes1,
            300 => ShelfAutoCloseMode.Minutes5,
            _ => fallback,
        };
    }

    private HistoryRetention GetHistoryRetention(
        IReadOnlyDictionary<string, string> raw,
        HistoryRetention fallback)
    {
        if (raw.TryGetValue(SettingKeys.HistoryRetention, out string? canonical) &&
            !string.IsNullOrWhiteSpace(canonical))
        {
            return GetEnum(raw, SettingKeys.HistoryRetention, fallback);
        }

        if (!raw.TryGetValue(SettingKeys.HistoryRetentionDaysLegacy, out string? legacy) ||
            string.IsNullOrWhiteSpace(legacy))
        {
            return fallback;
        }

        if (!int.TryParse(legacy, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days))
        {
            LogUnrecognizedSetting(legacy, SettingKeys.HistoryRetentionDaysLegacy);
            return fallback;
        }

        return days switch
        {
            0 => HistoryRetention.Disabled,
            1 => HistoryRetention.OneDay,
            7 => HistoryRetention.SevenDays,
            30 => HistoryRetention.ThirtyDays,
            _ => fallback,
        };
    }

    private static string GetSpeechWhisperModel(
        IReadOnlyDictionary<string, string> raw,
        string fallback,
        int loadedVersion)
    {
        string model = GetRequiredString(raw, SettingKeys.SpeechWhisperModel, fallback).Trim();
        if (loadedVersion < 2 && IsLegacyLowQualitySpeechDefault(model))
        {
            return fallback;
        }

        return model;
    }

    /// <summary>
    /// v4 made local Parakeet the default speech engine. Persisted "whisper"
    /// was the pre-v4 default, so those users move to Parakeet once (Whisper
    /// remains selectable in Settings); legacy provider values are normalized to local engines.
    /// </summary>
    private static string GetSpeechProviderWithMigration(
        IReadOnlyDictionary<string, string> raw,
        string fallback,
        int loadedVersion)
    {
        string provider = GetRequiredString(raw, SettingKeys.SpeechProvider, fallback).Trim();
        if (loadedVersion < 4 &&
            string.Equals(provider, SpeechSettings.WhisperProvider, StringComparison.OrdinalIgnoreCase))
        {
            return SpeechSettings.ParakeetProvider;
        }

        return provider;
    }

    /// <summary>
    /// v3 introduced the dark-first "obsidian glass" identity. Users who never
    /// made an explicit theme choice (persisted value is the old System
    /// default) are moved to Dark once; an explicit Light/Dark choice is kept.
    /// </summary>
    private ThemePreference GetThemeWithMigration(
        IReadOnlyDictionary<string, string> raw,
        ThemePreference fallback,
        int loadedVersion)
    {
        ThemePreference theme = GetEnum(raw, SettingKeys.GeneralTheme, fallback);
        if (loadedVersion < 3 && theme == ThemePreference.System)
        {
            return ThemePreference.Dark;
        }

        return theme;
    }

    private static bool IsLegacyLowQualitySpeechDefault(string model)
        => string.Equals(model, "tiny", StringComparison.OrdinalIgnoreCase)
           || string.Equals(model, "tiny.en", StringComparison.OrdinalIgnoreCase)
           || string.Equals(model, "base.en", StringComparison.OrdinalIgnoreCase);

    private static string GetSpeechLanguage(
        IReadOnlyDictionary<string, string> raw,
        string fallback,
        int loadedVersion)
    {
        string language = GetString(raw, SettingKeys.SpeechLanguage, fallback).Trim();
        if (loadedVersion < 2 && string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return language;
    }

    private static bool HasPersistedSpeechSettings(IReadOnlyDictionary<string, string> raw)
        => raw.ContainsKey(SettingKeys.SpeechProvider)
           || raw.ContainsKey(SettingKeys.SpeechWhisperModel)
           || raw.ContainsKey(SettingKeys.SpeechOpenAiModel)
           || raw.ContainsKey(SettingKeys.SpeechLanguage)
           || raw.ContainsKey(SettingKeys.SpeechInsertionMode)
           || raw.ContainsKey(SettingKeys.SpeechCustomDictionary);

    private HotkeyGesture GetHotkey(IReadOnlyDictionary<string, string> raw, string key, HotkeyGesture fallback)
    {
        if (!raw.TryGetValue(key, out string? v))
        {
            return fallback;
        }

        // An explicitly empty value means "no hotkey" (unassigned).
        if (string.IsNullOrWhiteSpace(v))
        {
            return HotkeyGesture.None;
        }

        if (HotkeyGesture.TryParse(v, out HotkeyGesture g))
        {
            return g;
        }

        LogUnparseableHotkey(v, key);
        return fallback;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Unrecognized value '{Value}' for setting '{Key}'; using default.")]
    private partial void LogUnrecognizedSetting(string value, string key);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Unparseable hotkey '{Value}' for setting '{Key}'; using default.")]
    private partial void LogUnparseableHotkey(string value, string key);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "A settings changed handler threw.")]
    private partial void LogSettingsChangedHandlerThrew(Exception exception);
}
