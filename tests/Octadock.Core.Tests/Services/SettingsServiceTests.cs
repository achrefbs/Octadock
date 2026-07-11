using FluentAssertions;
using NSubstitute;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Hotkeys;
using Octadock.Core.Persistence;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Octadock.Core.Tests.Fakes;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_applies_defaults_for_empty_store()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Should().BeEquivalentTo(OctadockSettings.Defaults);
    }

    [Fact]
    public async Task LoadAsync_reads_persisted_values_and_defaults_missing_ones()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.GeneralLaunchAtLogin] = "true",
            [SettingKeys.CaptureFilenameTemplate] = "Shot-{yyyy}",
            [SettingKeys.HistoryRetention] = "SevenDays",
            [SettingKeys.SpeechWhisperModel] = "small.en",
            [SettingKeys.SpeechOpenAiModel] = "gpt-4o-mini-transcribe",
            [SettingKeys.SpeechLanguage] = string.Empty,
            [SettingKeys.SpeechInsertionMode] = "clipboard",
            [SettingKeys.SpeechCustomDictionary] = "equals equals => ==",
            // history.enabled intentionally absent -> should default to true.
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.General.LaunchAtLogin.Should().BeTrue();
        service.Current.Capture.FilenameTemplate.Should().Be("Shot-{yyyy}");
        service.Current.History.Retention.Should().Be(HistoryRetention.SevenDays);
        service.Current.History.Enabled.Should().BeTrue(); // default
        service.Current.Speech.Provider.Should().Be(SpeechSettings.DefaultProvider);
        service.Current.Speech.WhisperModel.Should().Be("small.en");
        service.Current.Speech.OpenAiModel.Should().Be("gpt-4o-mini-transcribe");
        service.Current.Speech.Language.Should().BeEmpty();
        service.Current.Speech.InsertionMode.Should().Be("clipboard");
        service.Current.Speech.CustomDictionary.Should().Be("equals equals => ==");
    }

    [Fact]
    public async Task LoadAsync_applies_speech_defaults_for_missing_keys()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Speech.Provider.Should().Be(SpeechSettings.DefaultProvider);
        service.Current.Speech.WhisperModel.Should().Be(SpeechSettings.DefaultWhisperModel);
        service.Current.Speech.OpenAiModel.Should().Be(SpeechSettings.DefaultOpenAiModel);
        service.Current.Speech.Language.Should().Be(SpeechSettings.DefaultLanguage);
        service.Current.Speech.InsertionMode.Should().Be(SpeechSettings.DefaultInsertionMode);
        service.Current.Speech.CustomDictionary.Should().BeEmpty();
    }

    [Theory]
    [InlineData("tiny")]
    [InlineData("tiny.en")]
    [InlineData("base.en")]
    public async Task LoadAsync_migrates_legacy_speech_defaults(string model)
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = "1",
            [SettingKeys.SpeechWhisperModel] = model,
            [SettingKeys.SpeechLanguage] = "en",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Version.Should().Be(OctadockSettings.Defaults.Version);
        service.Current.Speech.WhisperModel.Should().Be(SpeechSettings.DefaultWhisperModel);
        service.Current.Speech.Language.Should().Be(SpeechSettings.DefaultLanguage);
    }

    [Fact]
    public async Task LoadAsync_preserves_base_model_after_speech_migration()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = OctadockSettings.Defaults.Version.ToString(),
            [SettingKeys.SpeechWhisperModel] = "base.en",
            [SettingKeys.SpeechLanguage] = "en",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Speech.WhisperModel.Should().Be("base.en");
        service.Current.Speech.Language.Should().Be("en");
    }

    [Fact]
    public async Task LoadAsync_treats_versionless_persisted_speech_model_as_legacy()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.SpeechWhisperModel] = "base.en",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Speech.WhisperModel.Should().Be(SpeechSettings.DefaultWhisperModel);
    }

    [Fact]
    public async Task LoadAsync_treats_versionless_persisted_speech_language_as_legacy()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.SpeechLanguage] = "en",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Speech.Language.Should().Be(SpeechSettings.DefaultLanguage);
    }

    [Fact]
    public async Task LoadAsync_disables_recording_audio_until_encoder_supports_it()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.RecordingIncludeMicrophone] = "true",
            [SettingKeys.RecordingIncludeSystemAudio] = "true",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Recording.IncludeMicrophone.Should().BeFalse();
        service.Current.Recording.IncludeSystemAudio.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_reads_legacy_documented_shelf_and_history_keys()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.ShelfAutoCloseSecondsLegacy] = "300",
            [SettingKeys.HistoryRetentionDaysLegacy] = "7",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Shelf.AutoClose.Should().Be(ShelfAutoCloseMode.Minutes5);
        service.Current.History.Retention.Should().Be(HistoryRetention.SevenDays);
    }

    [Fact]
    public async Task LoadAsync_prefers_canonical_settings_over_legacy_aliases()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.ShelfAutoClose] = nameof(ShelfAutoCloseMode.Seconds30),
            [SettingKeys.ShelfAutoCloseSecondsLegacy] = "300",
            [SettingKeys.HistoryRetention] = nameof(HistoryRetention.OneDay),
            [SettingKeys.HistoryRetentionDaysLegacy] = "30",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Shelf.AutoClose.Should().Be(ShelfAutoCloseMode.Seconds30);
        service.Current.History.Retention.Should().Be(HistoryRetention.OneDay);
    }

    [Fact]
    public async Task LoadAsync_parses_bools_robustly()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.GeneralLaunchAtLogin] = "1",
            [SettingKeys.GeneralShowTrayIcon] = "no",
            [SettingKeys.CaptureIncludeCursor] = "YES",
            [SettingKeys.CaptureFreezeScreen] = "off",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.General.LaunchAtLogin.Should().BeTrue();
        service.Current.General.ShowTrayIcon.Should().BeFalse();
        service.Current.Capture.IncludeCursor.Should().BeTrue();
        service.Current.Capture.FreezeScreen.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_falls_back_on_bad_enum_and_int()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.CaptureDefaultAction] = "not-an-action",
            [SettingKeys.CaptureJpegQuality] = "abc",
            [SettingKeys.ShortcutCaptureArea] = "!!invalid!!",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Capture.DefaultAction.Should().Be(OctadockSettings.Defaults.Capture.DefaultAction);
        service.Current.Capture.JpegQuality.Should().Be(OctadockSettings.Defaults.Capture.JpegQuality);
        service.Current.Shortcuts.CaptureArea.Should().Be(OctadockSettings.Defaults.Shortcuts.CaptureArea);
    }

    [Fact]
    public async Task LoadAsync_clamps_out_of_range_numbers_and_defaults_empty_filename_template()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.CaptureFilenameTemplate] = " ",
            [SettingKeys.CaptureJpegQuality] = "250",
            [SettingKeys.CaptureSelfTimerSeconds] = "-5",
            [SettingKeys.ShelfMarginDip] = "-10",
            [SettingKeys.ShelfMaxItems] = "99",
            [SettingKeys.RecordingFps] = "3",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Capture.FilenameTemplate.Should().Be(OctadockSettings.Defaults.Capture.FilenameTemplate);
        service.Current.Capture.JpegQuality.Should().Be(100);
        service.Current.Capture.SelfTimerSeconds.Should().Be(0);
        service.Current.Shelf.MarginDip.Should().Be(0);
        service.Current.Shelf.MaxItems.Should().Be(32);
        service.Current.Recording.Fps.Should().Be(10);
    }

    [Fact]
    public async Task SaveAsync_then_LoadAsync_round_trips_all_sections()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        var settings = OctadockSettings.Defaults with
        {
            General = new GeneralSettings
            {
                LaunchAtLogin = true,
                ShowTaskbarIcon = true,
                Theme = ThemePreference.Dark,
                FirstRunCompleted = true,
                CrashReportingEnabled = true,
            },
            Dock = new DockSettings
            {
                Enabled = false,
                HasCustomAnchor = true,
                AnchorX = 1280,
                AnchorY = 700,
            },
            Capture = new CaptureSettings
            {
                DefaultAction = PostCaptureAction.Copy,
                SaveDirectory = @"D:\Shots",
                FilenameTemplate = "Cap {yyyy}-{MM}",
                IncludeCursor = true,
                ImageFormat = CaptureImageFormat.Jpeg,
                JpegQuality = 75,
                SelfTimerSeconds = 10,
                ImageEditSaveBehavior = ImageEditSaveBehavior.CreateCopy,
            },
            Shelf = new ShelfSettings
            {
                Anchor = ShelfAnchor.TopRight,
                Size = ShelfSize.Large,
                AutoClose = ShelfAutoCloseMode.Minutes5,
                PeekBehavior = ShelfPeekBehavior.MoveToClearCorner,
                MarginDip = 32,
                MaxItems = 12,
                ShowChrome = true,
            },
            History = new HistorySettings { Enabled = false, Retention = HistoryRetention.Forever },
            Ocr = new OcrSettings
            {
                Provider = OcrProvider.Tesseract,
                OutputMode = OcrTextMode.Layout,
                PreferredLanguage = "en-US",
            },
            Speech = new SpeechSettings
            {
                Provider = "whisper",
                WhisperModel = "tiny.en",
                OpenAiModel = "gpt-4o-mini-transcribe",
                Language = string.Empty,
                InsertionMode = "clipboard",
                CustomDictionary = "fat arrow => =>",
            },
            Recording = new RecordingSettings
            {
                Fps = 60,
                Quality = RecordingQuality.High,
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
            },
            Automation = new AutomationSettings { ProtocolEnabled = false, CliEnabled = false },
        };

        await service.SaveAsync(settings);

        // Reload through a fresh service over the same store.
        var reloaded = new SettingsService(store);
        await reloaded.LoadAsync();

        reloaded.Current.Should().BeEquivalentTo(settings with
        {
            Recording = settings.Recording with
            {
                IncludeMicrophone = false,
                IncludeSystemAudio = false,
            },
        });
    }

    [Fact]
    public async Task SaveAsync_persists_hotkeys_as_strings()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        var settings = OctadockSettings.Defaults with
        {
            Shortcuts = OctadockSettings.Defaults.Shortcuts with
            {
                CaptureArea = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, (uint)'K'),
                Dictation = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, (uint)'D'),
            },
        };

        await service.SaveAsync(settings);

        store.Snapshot[SettingKeys.ShortcutCaptureArea].Should().Be("Ctrl+Alt+K");
        store.Snapshot[SettingKeys.ShortcutDictation].Should().Be("Ctrl+Alt+D");
    }

    [Fact]
    public async Task SaveAsync_persists_enums_by_name()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        await service.SaveAsync(OctadockSettings.Defaults with
        {
            Shelf = OctadockSettings.Defaults.Shelf with
            {
                Anchor = ShelfAnchor.TopLeft,
                PeekBehavior = ShelfPeekBehavior.FadeInPlace,
            },
        });

        store.Snapshot[SettingKeys.ShelfAnchor].Should().Be("TopLeft");
        store.Snapshot[SettingKeys.ShelfPeekBehavior].Should().Be("FadeInPlace");
    }

    [Fact]
    public async Task SaveAsync_persists_the_optional_shelf_frame_choice()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        await service.SaveAsync(OctadockSettings.Defaults with
        {
            Shelf = OctadockSettings.Defaults.Shelf with { ShowChrome = true },
        });

        store.Snapshot[SettingKeys.ShelfShowChrome].Should().Be("true");

        var reloaded = new SettingsService(store);
        await reloaded.LoadAsync();
        reloaded.Current.Shelf.ShowChrome.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_persists_speech_settings()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        await service.SaveAsync(OctadockSettings.Defaults with
        {
            Speech = OctadockSettings.Defaults.Speech with
            {
                WhisperModel = "small.en",
                OpenAiModel = "gpt-4o-mini-transcribe",
                Language = string.Empty,
                InsertionMode = "clipboard",
                CustomDictionary = "log line => Console.WriteLine",
            },
        });

        store.Snapshot[SettingKeys.SpeechProvider].Should().Be(SpeechSettings.DefaultProvider);
        store.Snapshot[SettingKeys.SpeechWhisperModel].Should().Be("small.en");
        store.Snapshot[SettingKeys.SpeechOpenAiModel].Should().Be("gpt-4o-mini-transcribe");
        store.Snapshot[SettingKeys.SpeechLanguage].Should().BeEmpty();
        store.Snapshot[SettingKeys.SpeechInsertionMode].Should().Be("clipboard");
        store.Snapshot[SettingKeys.SpeechCustomDictionary].Should().Be("log line => Console.WriteLine");
    }

    [Fact]
    public async Task SaveAsync_updates_current_snapshot()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        var updated = OctadockSettings.Defaults with
        {
            General = OctadockSettings.Defaults.General with { LaunchAtLogin = true },
        };
        await service.SaveAsync(updated);

        service.Current.General.LaunchAtLogin.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_raises_changed_event()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        SettingsChangedEventArgs? captured = null;
        service.Changed += (_, e) => captured = e;

        var updated = OctadockSettings.Defaults with
        {
            Capture = OctadockSettings.Defaults.Capture with { SelfTimerSeconds = 20 },
        };
        await service.SaveAsync(updated);

        captured.Should().NotBeNull();
        captured!.Settings.Capture.SelfTimerSeconds.Should().Be(20);
    }

    [Fact]
    public async Task SaveAsync_continues_notifying_when_changed_handler_throws()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        bool secondHandlerRan = false;
        service.Changed += (_, _) => throw new InvalidOperationException("boom");
        service.Changed += (_, _) => secondHandlerRan = true;

        await service.SaveAsync(OctadockSettings.Defaults with
        {
            General = OctadockSettings.Defaults.General with { Theme = ThemePreference.Dark },
        });

        secondHandlerRan.Should().BeTrue();
        service.Current.General.Theme.Should().Be(ThemePreference.Dark);
    }

    [Fact]
    public async Task UpdateAsync_applies_mutation_and_persists()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        await service.UpdateAsync(s => s with
        {
            History = s.History with { Retention = HistoryRetention.OneDay },
        });

        service.Current.History.Retention.Should().Be(HistoryRetention.OneDay);
        store.Snapshot[SettingKeys.HistoryRetention].Should().Be("OneDay");
    }

    [Fact]
    public async Task UpdateAsync_persists_dragged_dock_anchor()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        await service.UpdateAsync(s => s with
        {
            Dock = s.Dock with { HasCustomAnchor = true, AnchorX = 640, AnchorY = 400 },
        });

        service.Current.Dock.HasCustomAnchor.Should().BeTrue();
        service.Current.Dock.AnchorX.Should().Be(640);
        service.Current.Dock.AnchorY.Should().Be(400);
        store.Snapshot[SettingKeys.DockHasCustomAnchor].Should().Be("true");
        store.Snapshot[SettingKeys.DockAnchorX].Should().Be("640");

        // A fresh service over the same store restores the saved anchor.
        var reloaded = new SettingsService(store);
        await reloaded.LoadAsync();
        reloaded.Current.Dock.AnchorY.Should().Be(400);
    }

    [Fact]
    public async Task UpdateAsync_serializes_mutations_against_inflight_saves()
    {
        var store = new DelayedFirstSetManySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        Task first = service.UpdateAsync(s => s with
        {
            General = s.General with { LaunchAtLogin = true },
        });
        await store.FirstSetManyStarted.WaitAsync(TimeSpan.FromSeconds(1));

        Task second = service.UpdateAsync(s => s with
        {
            General = s.General with { Theme = ThemePreference.Dark },
        });

        await store.WaitForSecondSetManyStartedAsync(TimeSpan.FromMilliseconds(250));
        store.ReleaseFirstSetMany();

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        service.Current.General.LaunchAtLogin.Should().BeTrue();
        service.Current.General.Theme.Should().Be(ThemePreference.Dark);
        store.Snapshot[SettingKeys.GeneralLaunchAtLogin].Should().Be("true");
        store.Snapshot[SettingKeys.GeneralTheme].Should().Be(nameof(ThemePreference.Dark));
    }

    [Fact]
    public async Task UpdateAsync_serializes_changed_events_with_persistence_order()
    {
        var store = new DelayedFirstSetManySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        var firstChangedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var themesInNotificationOrder = new List<ThemePreference>();

        service.Changed += (_, e) =>
        {
            themesInNotificationOrder.Add(e.Settings.General.Theme);
            if (e.Settings.General.LaunchAtLogin)
            {
                firstChangedStarted.TrySetResult();
                releaseFirstChanged.Task.GetAwaiter().GetResult();
            }
        };

        Task first = service.UpdateAsync(s => s with
        {
            General = s.General with { LaunchAtLogin = true },
        });
        await store.FirstSetManyStarted.WaitAsync(TimeSpan.FromSeconds(1));

        Task second = service.UpdateAsync(s => s with
        {
            General = s.General with { Theme = ThemePreference.Light },
        });

        store.ReleaseFirstSetMany();
        await firstChangedStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        bool secondReachedStoreBeforeFirstNotificationFinished =
            await store.WaitForSecondSetManyStartedAsync(TimeSpan.FromMilliseconds(150));
        secondReachedStoreBeforeFirstNotificationFinished.Should().BeFalse();

        releaseFirstChanged.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        // Dark is the v3 default theme; the second update switches to Light.
        themesInNotificationOrder.Should().Equal(ThemePreference.Dark, ThemePreference.Light);
    }

    [Fact]
    public async Task Load_migrates_v2_system_theme_to_dark()
    {
        var store = new InMemorySettingsStore();
        await store.SetManyAsync(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = "2",
            [SettingKeys.GeneralTheme] = "System",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.General.Theme.Should().Be(ThemePreference.Dark);
        service.Current.Version.Should().Be(OctadockSettings.Defaults.Version);
    }

    [Fact]
    public async Task Load_keeps_explicit_theme_choices_across_the_v3_migration()
    {
        var store = new InMemorySettingsStore();
        await store.SetManyAsync(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = "2",
            [SettingKeys.GeneralTheme] = "Light",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.General.Theme.Should().Be(ThemePreference.Light);
    }

    [Fact]
    public async Task Load_respects_system_theme_chosen_after_v3()
    {
        var store = new InMemorySettingsStore();
        await store.SetManyAsync(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = "3",
            [SettingKeys.GeneralTheme] = "System",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.General.Theme.Should().Be(ThemePreference.System);
    }

    [Fact]
    public async Task Load_migrates_v3_whisper_provider_to_parakeet()
    {
        var store = new InMemorySettingsStore();
        await store.SetManyAsync(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = "3",
            [SettingKeys.SpeechProvider] = "whisper",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Speech.Provider.Should().Be(SpeechSettings.ParakeetProvider);
        service.Current.Version.Should().Be(OctadockSettings.Defaults.Version);
    }

    [Fact]
    public async Task Load_keeps_explicit_cloud_provider_across_the_v4_migration()
    {
        var store = new InMemorySettingsStore();
        await store.SetManyAsync(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = "3",
            [SettingKeys.SpeechProvider] = "openai",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Speech.Provider.Should().Be(SpeechSettings.OpenAiProvider);
    }

    [Fact]
    public async Task Load_respects_whisper_chosen_after_v4()
    {
        var store = new InMemorySettingsStore();
        await store.SetManyAsync(new Dictionary<string, string>
        {
            [SettingKeys.SettingsVersion] = "4",
            [SettingKeys.SpeechProvider] = "whisper",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Speech.Provider.Should().Be(SpeechSettings.WhisperProvider);
    }

    [Fact]
    public async Task UpdateAsync_null_mutation_result_throws()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        Func<Task> act = () => service.UpdateAsync(_ => null!);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveAsync_writes_the_full_flat_set_in_one_call()
    {
        var store = Substitute.For<ISettingsStore>();
        store.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>()));
        var service = new SettingsService(store);

        await service.SaveAsync(OctadockSettings.Defaults);

        await store.Received(1).SetManyAsync(
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d.ContainsKey(SettingKeys.SettingsVersion) &&
                d.ContainsKey(SettingKeys.ShortcutRecord) &&
                d.ContainsKey(SettingKeys.ShortcutDictation) &&
                d.ContainsKey(SettingKeys.SpeechWhisperModel) &&
                d.ContainsKey(SettingKeys.SpeechOpenAiModel) &&
                d.ContainsKey(SettingKeys.SpeechCustomDictionary) &&
                d.ContainsKey(SettingKeys.AutomationCliEnabled)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_hotkey_string_is_treated_as_unassigned()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.ShortcutOcr] = string.Empty,
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Shortcuts.Ocr.Should().Be(HotkeyGesture.None);
    }

    private sealed class DelayedFirstSetManySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly TaskCompletionSource _firstSetManyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondSetManyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstSetMany = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _setManyCallCount;

        public Task FirstSetManyStarted => _firstSetManyStarted.Task;

        public IReadOnlyDictionary<string, string> Snapshot
        {
            get
            {
                lock (_gate)
                {
                    return new Dictionary<string, string>(_data, StringComparer.Ordinal);
                }
            }
        }

        public async Task<bool> WaitForSecondSetManyStartedAsync(TimeSpan timeout)
            => await Task.WhenAny(_secondSetManyStarted.Task, Task.Delay(timeout)) == _secondSetManyStarted.Task;

        public void ReleaseFirstSetMany() => _releaseFirstSetMany.TrySetResult();

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string>(_data, StringComparer.Ordinal));
            }
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_data.TryGetValue(key, out string? value) ? value : null);
            }
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _data[key] = value;
            }

            return Task.CompletedTask;
        }

        public async Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref _setManyCallCount);
            if (call == 1)
            {
                _firstSetManyStarted.TrySetResult();
                await _releaseFirstSetMany.Task.WaitAsync(cancellationToken);
            }
            else if (call == 2)
            {
                _secondSetManyStarted.TrySetResult();
            }

            lock (_gate)
            {
                foreach (KeyValuePair<string, string> pair in values)
                {
                    _data[pair.Key] = pair.Value;
                }
            }
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _data.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}
