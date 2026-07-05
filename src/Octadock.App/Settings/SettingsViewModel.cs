using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Hotkeys;
using Octadock.Core.Ipc;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.App.Settings;

/// <summary>
/// View model for the settings window. Loads a working copy from
/// <see cref="ISettingsService.Current"/>, exposes every tab's fields as bindable
/// properties, and persists a rebuilt <see cref="OctadockSettings"/> on Save.
/// Side-effecting toggles (launch-at-login, protocol registration, hotkey
/// registration) are applied through the relevant Core services when saved.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IHotkeyService _hotkeys;
    private readonly IStartupRegistration _startup;
    private readonly IProtocolRegistration _protocol;
    private readonly IOcrProviderFactory _ocrFactory;
    private readonly ISpeechToTextProviderFactory _speechFactory;
    private readonly ISpeechToTextProvider[] _speechProviders;
    private readonly ICaptureRepository _captureRepository;
    private readonly ICommandFormatter _commandFormatter;
    private readonly ILogger<SettingsViewModel> _logger;

    // ---- General ----
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private bool _showTrayIcon;
    [ObservableProperty] private bool _showTaskbarIcon;
    [ObservableProperty] private ThemePreference _theme;
    [ObservableProperty] private bool _crashReportingEnabled;
    [ObservableProperty] private bool _showDock;

    // ---- Capture ----
    [ObservableProperty] private PostCaptureAction _defaultAction;
    [ObservableProperty] private string _saveDirectory = string.Empty;
    [ObservableProperty] private string _filenameTemplate = string.Empty;
    [ObservableProperty] private bool _includeCursor;
    [ObservableProperty] private bool _windowShadow;
    [ObservableProperty] private bool _excludeOctadockWindows;
    [ObservableProperty] private MultiMonitorCaptureMode _multiMonitorMode;
    [ObservableProperty] private CaptureImageFormat _imageFormat;
    [ObservableProperty] private int _jpegQuality;
    [ObservableProperty] private int _selfTimerSeconds;
    [ObservableProperty] private bool _freezeScreen;

    // ---- Shelf ----
    [ObservableProperty] private ShelfAnchor _shelfAnchor;
    [ObservableProperty] private ShelfSize _shelfSize;
    [ObservableProperty] private ShelfAutoCloseMode _shelfAutoClose;
    [ObservableProperty] private bool _shelfRestoreEnabled;
    [ObservableProperty] private int _shelfMarginDip;
    [ObservableProperty] private int _shelfMaxItems;

    // ---- History ----
    [ObservableProperty] private bool _historyEnabled;
    [ObservableProperty] private HistoryRetention _historyRetention;

    // ---- Clipboard history ----
    [ObservableProperty] private bool _clipboardMonitorEnabled;
    [ObservableProperty] private bool _clipboardIncludeImages;
    [ObservableProperty] private int _clipboardMaxItems;

    // ---- OCR ----
    [ObservableProperty] private OcrProvider _ocrProvider;
    [ObservableProperty] private OcrTextMode _ocrOutputMode;
    [ObservableProperty] private string _ocrPreferredLanguage = string.Empty;
    [ObservableProperty] private string _ocrAvailabilityText = string.Empty;

    // ---- Speech ----
    [ObservableProperty] private string _speechProvider = SpeechSettings.DefaultProvider;
    [ObservableProperty] private string _speechActivationMode = SpeechSettings.DefaultActivationMode;
    [ObservableProperty] private string _speechWhisperModel = SpeechSettings.DefaultWhisperModel;
    [ObservableProperty] private string _speechOpenAiModel = SpeechSettings.DefaultOpenAiModel;
    [ObservableProperty] private string _speechLanguage = SpeechSettings.DefaultLanguage;
    [ObservableProperty] private string _speechInsertionMode = SpeechSettings.DefaultInsertionMode;
    [ObservableProperty] private string _speechCustomDictionary = string.Empty;
    [ObservableProperty] private string _speechAvailabilityText = string.Empty;
    [ObservableProperty] private bool _speechLivePartials = true;
    [ObservableProperty] private bool _speechAutoStopOnSilence;

    // ---- Recording ----
    [ObservableProperty] private int _recordingFps;
    [ObservableProperty] private RecordingQuality _recordingQuality;
    [ObservableProperty] private bool _recordingIncludeCursor;
    [ObservableProperty] private bool _recordingIncludeMicrophone;
    [ObservableProperty] private bool _recordingIncludeSystemAudio;

    // ---- Automation ----
    [ObservableProperty] private bool _protocolEnabled;
    [ObservableProperty] private bool _cliEnabled;

    // ---- Status ----
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Creates the settings view model and loads a working copy.</summary>
    public SettingsViewModel(
        ISettingsService settings,
        IHotkeyService hotkeys,
        IStartupRegistration startup,
        IProtocolRegistration protocol,
        IOcrProviderFactory ocrFactory,
        ISpeechToTextProviderFactory speechFactory,
        IEnumerable<ISpeechToTextProvider> speechProviders,
        ICaptureRepository captureRepository,
        ICommandFormatter commandFormatter,
        ILogger<SettingsViewModel> logger)
    {
        _speechProviders = speechProviders.ToArray();
        _settings = settings;
        _hotkeys = hotkeys;
        _startup = startup;
        _protocol = protocol;
        _ocrFactory = ocrFactory;
        _speechFactory = speechFactory;
        _captureRepository = captureRepository;
        _commandFormatter = commandFormatter;
        _logger = logger;

        Shortcuts = new ObservableCollection<HotkeyGestureViewModel>();
        Load(_settings.Current);
        DescribeOcr();
        DescribeSpeech();
        BuildAutomationExamples();
    }

    /// <summary>The available theme choices for the General tab.</summary>
    public IReadOnlyList<ThemePreference> ThemeOptions { get; } =
        [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark];

    /// <summary>The available post-capture actions.</summary>
    public IReadOnlyList<PostCaptureAction> ActionOptions { get; } =
        [PostCaptureAction.Shelf, PostCaptureAction.Copy, PostCaptureAction.Save, PostCaptureAction.Annotate, PostCaptureAction.Pin, PostCaptureAction.Discard];

    /// <summary>Multi-monitor capture modes.</summary>
    public IReadOnlyList<MultiMonitorCaptureMode> MultiMonitorOptions { get; } =
        [MultiMonitorCaptureMode.ActiveMonitor, MultiMonitorCaptureMode.SelectedMonitor, MultiMonitorCaptureMode.AllMonitors];

    /// <summary>Image formats.</summary>
    public IReadOnlyList<CaptureImageFormat> ImageFormatOptions { get; } = [CaptureImageFormat.Png, CaptureImageFormat.Jpeg];

    /// <summary>Shelf anchors.</summary>
    public IReadOnlyList<ShelfAnchor> ShelfAnchorOptions { get; } =
        [ShelfAnchor.BottomLeft, ShelfAnchor.BottomRight, ShelfAnchor.TopLeft, ShelfAnchor.TopRight];

    /// <summary>Shelf sizes.</summary>
    public IReadOnlyList<ShelfSize> ShelfSizeOptions { get; } = [ShelfSize.Small, ShelfSize.Medium, ShelfSize.Large];

    /// <summary>Shelf auto-close options.</summary>
    public IReadOnlyList<ShelfAutoCloseMode> ShelfAutoCloseOptions { get; } =
        [ShelfAutoCloseMode.Never, ShelfAutoCloseMode.AfterAction, ShelfAutoCloseMode.Seconds30, ShelfAutoCloseMode.Minutes1, ShelfAutoCloseMode.Minutes5];

    /// <summary>History retention options.</summary>
    public IReadOnlyList<HistoryRetention> RetentionOptions { get; } =
        [HistoryRetention.Disabled, HistoryRetention.OneDay, HistoryRetention.SevenDays, HistoryRetention.ThirtyDays, HistoryRetention.Forever];

    /// <summary>OCR providers.</summary>
    public IReadOnlyList<OcrProvider> OcrProviderOptions { get; } =
        [OcrProvider.WindowsMediaOcr, OcrProvider.WindowsAiTextRecognition, OcrProvider.Tesseract];

    /// <summary>OCR output modes.</summary>
    public IReadOnlyList<OcrTextMode> OcrModeOptions { get; } = [OcrTextMode.Compact, OcrTextMode.Lines, OcrTextMode.Layout];

    /// <summary>Speech providers, labeled with live availability from Describe().</summary>
    public ObservableCollection<SpeechProviderOption> SpeechProviderOptions { get; } = [];

    /// <summary>Local speech models with download/delete management.</summary>
    public ObservableCollection<SpeechModelRowViewModel> SpeechModels { get; } = [];

    /// <summary>Dictation activation modes (toggle hotkey, hold-to-talk, or both).</summary>
    public IReadOnlyList<string> SpeechActivationModeOptions { get; } =
        [SpeechSettings.ActivationModeToggle, SpeechSettings.ActivationModeHold, SpeechSettings.ActivationModeBoth];

    /// <summary>Local Whisper models exposed for dictation tests.</summary>
    public IReadOnlyList<string> SpeechWhisperModelOptions { get; } =
        ["small", "small.en", "medium", "medium.en", "base.en", "base", "tiny.en", "tiny"];

    /// <summary>OpenAI transcription models exposed for opt-in cloud dictation.</summary>
    public IReadOnlyList<string> SpeechOpenAiModelOptions { get; } =
        [SpeechSettings.DefaultOpenAiModel, "gpt-4o-mini-transcribe", "whisper-1"];

    /// <summary>Dictation insertion modes.</summary>
    public IReadOnlyList<string> SpeechInsertionModeOptions { get; } = ["paste", "clipboard"];

    /// <summary>Recording quality presets.</summary>
    public IReadOnlyList<RecordingQuality> RecordingQualityOptions { get; } =
        [RecordingQuality.Low, RecordingQuality.Medium, RecordingQuality.High];

    /// <summary>Editable shortcut rows for the Shortcuts tab.</summary>
    public ObservableCollection<HotkeyGestureViewModel> Shortcuts { get; }

    /// <summary>Example automation commands shown on the Automation tab.</summary>
    public ObservableCollection<string> AutomationExamples { get; } = new();

    /// <summary>Raised when Save completes successfully so the window can close.</summary>
    public event EventHandler? Saved;

    private void Load(OctadockSettings s)
    {
        LaunchAtLogin = s.General.LaunchAtLogin;
        ShowTrayIcon = s.General.ShowTrayIcon;
        ShowTaskbarIcon = s.General.ShowTaskbarIcon;
        Theme = s.General.Theme;
        CrashReportingEnabled = s.General.CrashReportingEnabled;
        ShowDock = s.Dock.Enabled;

        DefaultAction = s.Capture.DefaultAction;
        SaveDirectory = s.Capture.SaveDirectory;
        FilenameTemplate = s.Capture.FilenameTemplate;
        IncludeCursor = s.Capture.IncludeCursor;
        WindowShadow = s.Capture.WindowShadow;
        ExcludeOctadockWindows = s.Capture.ExcludeOctadockWindows;
        MultiMonitorMode = s.Capture.MultiMonitorMode;
        ImageFormat = s.Capture.ImageFormat;
        JpegQuality = s.Capture.JpegQuality;
        SelfTimerSeconds = s.Capture.SelfTimerSeconds;
        FreezeScreen = s.Capture.FreezeScreen;

        ShelfAnchor = s.Shelf.Anchor;
        ShelfSize = s.Shelf.Size;
        ShelfAutoClose = s.Shelf.AutoClose;
        ShelfRestoreEnabled = s.Shelf.RestoreEnabled;
        ShelfMarginDip = s.Shelf.MarginDip;
        ShelfMaxItems = s.Shelf.MaxItems;

        HistoryEnabled = s.History.Enabled;
        HistoryRetention = s.History.Retention;

        ClipboardMonitorEnabled = s.Clipboard.MonitorEnabled;
        ClipboardIncludeImages = s.Clipboard.IncludeImages;
        ClipboardMaxItems = s.Clipboard.MaxItems;

        OcrProvider = s.Ocr.Provider;
        OcrOutputMode = s.Ocr.OutputMode;
        OcrPreferredLanguage = s.Ocr.PreferredLanguage;

        SpeechProvider = s.Speech.Provider;
        SpeechActivationMode = s.Speech.ActivationMode;
        SpeechLivePartials = s.Speech.LivePartials;
        SpeechAutoStopOnSilence = s.Speech.AutoStopOnSilence;
        SpeechWhisperModel = s.Speech.WhisperModel;
        SpeechOpenAiModel = s.Speech.OpenAiModel;
        SpeechLanguage = s.Speech.Language;
        SpeechInsertionMode = s.Speech.InsertionMode;
        SpeechCustomDictionary = s.Speech.CustomDictionary;

        RecordingFps = s.Recording.Fps;
        RecordingQuality = s.Recording.Quality;
        RecordingIncludeCursor = s.Recording.IncludeCursor;
        RecordingIncludeMicrophone = false;
        RecordingIncludeSystemAudio = false;

        ProtocolEnabled = s.Automation.ProtocolEnabled;
        CliEnabled = s.Automation.CliEnabled;

        Shortcuts.Clear();
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.CaptureArea, "Capture area", s.Shortcuts.CaptureArea));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.CaptureWindow, "Capture window", s.Shortcuts.CaptureWindow));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.CaptureFullscreen, "Capture fullscreen", s.Shortcuts.CaptureFullscreen));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.CapturePreviousArea, "Capture previous area", s.Shortcuts.CapturePreviousArea));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.AllInOne, "All-in-one HUD", s.Shortcuts.AllInOne));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.Dictation, "Toggle dictation", s.Shortcuts.Dictation));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.Ocr, "Capture text (OCR)", s.Shortcuts.Ocr));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.Record, "Record screen", s.Shortcuts.Record));
        Shortcuts.Add(new HotkeyGestureViewModel(HotkeyAction.ClipboardHistory, "Clipboard history", s.Shortcuts.ClipboardHistory));
    }

    private OctadockSettings Build()
    {
        OctadockSettings current = _settings.Current;
        bool showTrayIcon = ShowTrayIcon;
        bool showTaskbarIcon = ShowTaskbarIcon;
        EnsureVisibleAffordance(ref showTrayIcon, ref showTaskbarIcon);

        return current with
        {
            General = current.General with
            {
                LaunchAtLogin = LaunchAtLogin,
                ShowTrayIcon = showTrayIcon,
                ShowTaskbarIcon = showTaskbarIcon,
                Theme = Theme,
                CrashReportingEnabled = CrashReportingEnabled,
            },
            Dock = current.Dock with
            {
                Enabled = ShowDock,
            },
            Capture = current.Capture with
            {
                DefaultAction = DefaultAction,
                SaveDirectory = SaveDirectory ?? string.Empty,
                FilenameTemplate = string.IsNullOrWhiteSpace(FilenameTemplate) ? current.Capture.FilenameTemplate : FilenameTemplate,
                IncludeCursor = IncludeCursor,
                WindowShadow = WindowShadow,
                ExcludeOctadockWindows = ExcludeOctadockWindows,
                MultiMonitorMode = MultiMonitorMode,
                ImageFormat = ImageFormat,
                JpegQuality = Math.Clamp(JpegQuality, 1, 100),
                SelfTimerSeconds = Math.Clamp(SelfTimerSeconds, 0, 60),
                FreezeScreen = FreezeScreen,
            },
            Shelf = current.Shelf with
            {
                Anchor = ShelfAnchor,
                Size = ShelfSize,
                AutoClose = ShelfAutoClose,
                RestoreEnabled = ShelfRestoreEnabled,
                MarginDip = Math.Clamp(ShelfMarginDip, 0, 200),
                MaxItems = Math.Clamp(ShelfMaxItems, 1, 32),
            },
            History = current.History with
            {
                Enabled = HistoryEnabled,
                Retention = HistoryRetention,
            },
            Clipboard = current.Clipboard with
            {
                MonitorEnabled = ClipboardMonitorEnabled,
                IncludeImages = ClipboardIncludeImages,
                MaxItems = Math.Clamp(ClipboardMaxItems, 20, 5000),
            },
            Ocr = current.Ocr with
            {
                Provider = OcrProvider,
                OutputMode = OcrOutputMode,
                PreferredLanguage = OcrPreferredLanguage ?? string.Empty,
            },
            Speech = current.Speech with
            {
                Provider = string.IsNullOrWhiteSpace(SpeechProvider) ? SpeechSettings.DefaultProvider : SpeechProvider.Trim(),
                ActivationMode = string.IsNullOrWhiteSpace(SpeechActivationMode) ? SpeechSettings.DefaultActivationMode : SpeechActivationMode.Trim(),
                LivePartials = SpeechLivePartials,
                AutoStopOnSilence = SpeechAutoStopOnSilence,
                WhisperModel = string.IsNullOrWhiteSpace(SpeechWhisperModel) ? SpeechSettings.DefaultWhisperModel : SpeechWhisperModel.Trim(),
                OpenAiModel = string.IsNullOrWhiteSpace(SpeechOpenAiModel) ? SpeechSettings.DefaultOpenAiModel : SpeechOpenAiModel.Trim(),
                Language = SpeechLanguage?.Trim() ?? string.Empty,
                InsertionMode = string.IsNullOrWhiteSpace(SpeechInsertionMode)
                    ? SpeechSettings.DefaultInsertionMode
                    : SpeechInsertionMode.Trim(),
                CustomDictionary = SpeechCustomDictionary ?? string.Empty,
            },
            Recording = current.Recording with
            {
                Fps = Math.Clamp(RecordingFps, 10, 60),
                Quality = RecordingQuality,
                IncludeCursor = RecordingIncludeCursor,
                IncludeMicrophone = false,
                IncludeSystemAudio = false,
            },
            Shortcuts = new ShortcutSettings
            {
                CaptureArea = GestureFor(HotkeyAction.CaptureArea),
                CaptureWindow = GestureFor(HotkeyAction.CaptureWindow),
                CaptureFullscreen = GestureFor(HotkeyAction.CaptureFullscreen),
                CapturePreviousArea = GestureFor(HotkeyAction.CapturePreviousArea),
                AllInOne = GestureFor(HotkeyAction.AllInOne),
                Dictation = GestureFor(HotkeyAction.Dictation),
                Ocr = GestureFor(HotkeyAction.Ocr),
                Record = GestureFor(HotkeyAction.Record),
                ClipboardHistory = GestureFor(HotkeyAction.ClipboardHistory),
            },
            Automation = current.Automation with
            {
                ProtocolEnabled = ProtocolEnabled,
                CliEnabled = CliEnabled,
            },
        };
    }

    partial void OnShowTrayIconChanged(bool value)
    {
        if (!value && !ShowTaskbarIcon)
        {
            ShowTaskbarIcon = true;
            StatusMessage = "At least one app access point must stay visible.";
        }
    }

    partial void OnShowTaskbarIconChanged(bool value)
    {
        if (!value && !ShowTrayIcon)
        {
            ShowTrayIcon = true;
            StatusMessage = "At least one app access point must stay visible.";
        }
    }

    private static void EnsureVisibleAffordance(ref bool showTrayIcon, ref bool showTaskbarIcon)
    {
        if (!showTrayIcon && !showTaskbarIcon)
        {
            showTaskbarIcon = true;
        }
    }

    private HotkeyGesture GestureFor(HotkeyAction action)
    {
        foreach (HotkeyGestureViewModel row in Shortcuts)
        {
            if (row.Action == action)
            {
                return row.Gesture;
            }
        }

        return HotkeyGesture.None;
    }

    /// <summary>Updates a pending shortcut edit and flags duplicate local gestures.</summary>
    public void ApplyGesture(HotkeyGestureViewModel row, HotkeyGesture gesture)
    {
        row.Gesture = gesture;

        if (UpdateDuplicateShortcutConflicts())
        {
            StatusMessage = "Shortcut updated. Choose Save to apply.";
            return;
        }

        StatusMessage = "Each shortcut must use a unique key combination.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            if (!UpdateDuplicateShortcutConflicts())
            {
                StatusMessage = "Each shortcut must use a unique key combination.";
                return;
            }

            OctadockSettings next = Build();

            bool hotkeysOk = RegisterSavedHotkeys(next.Shortcuts);
            if (!hotkeysOk)
            {
                StatusMessage = "Could not save settings: one or more shortcuts could not be registered.";
                return;
            }

            try
            {
                await _settings.SaveAsync(next).ConfigureAwait(true);
            }
            catch
            {
                RegisterSavedHotkeys(_settings.Current.Shortcuts);
                throw;
            }

            // Apply registry side effects after persisting intent; startup sync
            // retries any registry side effects that fail here.
            bool launchOk = ApplyLaunchAtLogin(next.General.LaunchAtLogin);
            bool protocolOk = ApplyProtocol(next.Automation.ProtocolEnabled);

            if (launchOk && protocolOk && hotkeysOk)
            {
                StatusMessage = "Settings saved.";
                Saved?.Invoke(this, EventArgs.Empty);
                return;
            }

            StatusMessage = BuildPartialSaveMessage(launchOk, protocolOk, hotkeysOk);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings.");
            StatusMessage = $"Could not save settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        Load(OctadockSettings.Defaults);
        UpdateDuplicateShortcutConflicts();
        StatusMessage = "Defaults loaded. Choose Save to apply.";
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        try
        {
            var older = await _captureRepository.GetOlderThanAsync(DateTimeOffset.MaxValue).ConfigureAwait(true);
            foreach (var record in older)
            {
                await _captureRepository.SoftDeleteAsync(record.Id, DateTimeOffset.Now).ConfigureAwait(true);
            }

            StatusMessage = $"Cleared {older.Count} capture(s) from history.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear history.");
            StatusMessage = $"Could not clear history: {ex.Message}";
        }
    }

    private bool ApplyLaunchAtLogin(bool enabled)
    {
        try
        {
            if (enabled && !_startup.IsEnabled())
            {
                _startup.Enable();
            }
            else if (!enabled)
            {
                _startup.Disable();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update launch-at-login.");
            return false;
        }
    }

    private bool ApplyProtocol(bool enabled)
    {
        try
        {
            if (enabled && !_protocol.IsRegistered())
            {
                _protocol.Register();
            }
            else if (!enabled)
            {
                _protocol.Unregister();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update protocol registration.");
            return false;
        }
    }

    private bool UpdateDuplicateShortcutConflicts()
    {
        var duplicateGestures = Shortcuts
            .Where(row => !row.Gesture.IsEmpty)
            .GroupBy(row => row.Gesture)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet();

        foreach (HotkeyGestureViewModel row in Shortcuts)
        {
            row.IsConflict = !row.Gesture.IsEmpty && duplicateGestures.Contains(row.Gesture);
        }

        return duplicateGestures.Count == 0;
    }

    private bool ApplyHotkeyRegistrationResults(IReadOnlyList<HotkeyRegistration> results)
    {
        var byAction = results.ToDictionary(result => result.Action);
        bool success = true;

        foreach (HotkeyGestureViewModel row in Shortcuts)
        {
            if (!byAction.TryGetValue(row.Action, out HotkeyRegistration? result))
            {
                row.IsConflict = false;
                continue;
            }

            row.IsConflict = !result.Success;
            success &= result.Success;

            if (result.IsConflict)
            {
                _logger.LogWarning(
                    "Hotkey for {Action} ({Gesture}) could not be registered: {Error}",
                    result.Action,
                    result.Gesture,
                    result.Error ?? "conflict");
            }
        }

        return success;
    }

    private bool RegisterSavedHotkeys(ShortcutSettings shortcuts)
    {
        try
        {
            return ApplyHotkeyRegistrationResults(_hotkeys.RegisterAll(shortcuts));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register saved hotkeys.");
            foreach (HotkeyGestureViewModel row in Shortcuts)
            {
                row.IsConflict = true;
            }

            return false;
        }
    }

    private static string BuildPartialSaveMessage(bool launchOk, bool protocolOk, bool hotkeysOk)
    {
        var issues = new List<string>(3);
        if (!launchOk)
        {
            issues.Add("launch-at-login could not be updated");
        }

        if (!protocolOk)
        {
            issues.Add("protocol registration could not be updated");
        }

        if (!hotkeysOk)
        {
            issues.Add("one or more shortcuts could not be registered");
        }

        return $"Settings saved, but {string.Join("; ", issues)}.";
    }

    private void DescribeOcr()
    {
        try
        {
            var lines = _ocrFactory.Describe()
                .Select(d => $"{d.Provider}: {(d.Availability.IsAvailable ? "available" : d.Availability.Reason ?? "unavailable")}");
            OcrAvailabilityText = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to describe OCR providers.");
            OcrAvailabilityText = "OCR provider availability is unknown.";
        }
    }

    private void DescribeSpeech()
    {
        try
        {
            IReadOnlyList<SpeechProviderDescription> described = _speechFactory.Describe();
            SpeechProviderOptions.Clear();
            foreach (SpeechProviderDescription d in described)
            {
                SpeechProviderOptions.Add(new SpeechProviderOption(
                    d.Id,
                    d.IsAvailable ? d.DisplayName : $"{d.DisplayName} — unavailable",
                    d.Reason ?? string.Empty));
            }

            SpeechAvailabilityText = string.Join(
                Environment.NewLine,
                described.Select(d =>
                    $"{d.DisplayName} ({d.Id}): {(d.IsAvailable ? d.Reason ?? "available" : d.Reason ?? "unavailable")}"));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to describe speech providers.");
            SpeechAvailabilityText = "Speech provider availability is unknown.";
        }

        RebuildSpeechModels();
    }

    /// <summary>
    /// Rebuilds the model-manager rows: the Parakeet model plus the currently
    /// selected Whisper variant. Rows with a download in flight are kept so
    /// their progress text survives a model-combo change.
    /// </summary>
    private void RebuildSpeechModels()
    {
        if (SpeechModels.Any(row => row.IsBusy))
        {
            return;
        }

        SpeechModels.Clear();
        foreach (ISpeechToTextProvider provider in _speechProviders)
        {
            if (provider is not IModelBackedSpeechProvider modelBacked)
            {
                continue;
            }

            if (string.Equals(provider.Id, SpeechSettings.ParakeetProvider, StringComparison.OrdinalIgnoreCase))
            {
                SpeechModels.Add(new SpeechModelRowViewModel(
                    modelBacked,
                    SpeechSettings.DefaultParakeetModel,
                    "Parakeet TDT 0.6B v3 (default engine)",
                    _logger));
            }
            else if (string.Equals(provider.Id, SpeechSettings.WhisperProvider, StringComparison.OrdinalIgnoreCase))
            {
                string model = string.IsNullOrWhiteSpace(SpeechWhisperModel)
                    ? SpeechSettings.DefaultWhisperModel
                    : SpeechWhisperModel.Trim();
                SpeechModels.Add(new SpeechModelRowViewModel(
                    modelBacked,
                    model,
                    $"Whisper {model} (fallback, 99 languages)",
                    _logger));
            }
        }
    }

    partial void OnSpeechWhisperModelChanged(string value) => RebuildSpeechModels();

    private void BuildAutomationExamples()
    {
        AutomationExamples.Clear();
        AutomationExamples.Add(_commandFormatter.ToUri(OctadockCommand.Create(CommandType.CaptureArea)));
        AutomationExamples.Add(_commandFormatter.ToUri(OctadockCommand.Create(
            CommandType.CaptureArea,
            new Dictionary<string, string> { ["action"] = "copy" })));
        AutomationExamples.Add(_commandFormatter.ToUri(OctadockCommand.Create(
            CommandType.CaptureFullscreen,
            new Dictionary<string, string> { ["monitor"] = "1", ["action"] = "save" })));
        AutomationExamples.Add("octadock.exe capture-area --action copy");
        AutomationExamples.Add("octadock.exe pin --filepath \"C:\\path\\to\\reference.png\"");
        AutomationExamples.Add("octadock.exe ocr --area 100,120,800,600 --mode lines");
        AutomationExamples.Add("octadock.exe settings --tab shortcuts");
        AutomationExamples.Add($"Pipe: {IpcProtocol.PipeName(Environment.UserName)}");
    }
}
