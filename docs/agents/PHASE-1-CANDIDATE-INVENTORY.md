# Phase 1 integration-candidate inventory

Updated 2026-07-19. This is the T-0 per-file inventory for the dirty Phase 1
candidate based on `219b487`. It describes the current diff; it does not claim
authorship of the preview/context work that was already in flight. Those changes
were preserved and extended in place. The candidate remains uncommitted so that
unrelated user work is not silently folded into an integration commit.

The final automated gate for this candidate is recorded in `docs/TESTING.md`.
Committed recovery foundations are `d8696f3` (release packaging), `8a26a8c`
(web validation), and `219b487` (Phase 1 acceptance harnesses).

## Build, evidence, and product truth

| File | Current-candidate purpose |
| --- | --- |
| `build/build.ps1` | Makes the documented Release command run version/copy/web, all test families, self-contained App/CLI publish, and the public-boundary gate. |
| `docs/ARCHITECTURE.md` | Records Context persistence and the additive IPC capture-ID response. |
| `docs/AUTOMATION.md` | Documents truthful capture cancellation and JSON `captureId` correlation. |
| `docs/CAPABILITIES.md` | Updates the shipped-capability truth for Context, reviewed handoff, Dock theming, and automation. |
| `docs/PLAN.md` | Records the locally implemented recovery slices and the remaining founder/manual gates. |
| `docs/PROJECT-STATE.md` | Updates current behavior, test counts, reliability work, and verification gaps. |
| `docs/ROADMAP.md` | Updates Gate C evidence/status without marking hardware/manual work complete. |
| `docs/TESTING.md` | Records the canonical four-layer gate and the latest deterministic evidence. |
| `docs/acceptance/PHASE-1-C04-C07.md` | Documents safe C-04…C-07 plan, measurement, isolation, and evidence commands. |
| `docs/agents/CONTEXT.md` | Updates the database migration and candidate test-count map. |
| `docs/agents/PHASE-1-CANDIDATE-INVENTORY.md` | Provides this per-file T-0 integration inventory. |
| `tests/acceptance/AcceptanceTooling.Tests.ps1` | Self-tests plan schemas, process measurement, exact capture correlation, quoted-path fingerprints, and the clean WPF scan. |
| `tools/acceptance/Acceptance.Common.psm1` | Fingerprints tracked diffs plus exact NUL-delimited untracked paths/content. |
| `tools/acceptance/Invoke-SoakAcceptance.ps1` | Runs acknowledged dedicated-profile soak probes and correlates exact capture IDs to SQLite/files. |
| `tools/acceptance/config/soak-profile.json` | Defines the honest 50 capture / 10 scrolling / 1 dictation soak contract. |
| `tools/acceptance/examples/probe-result.capture.example.json` | Shows the required command capture-ID evidence field. |
| `tools/branding/validate-logo-assets.ps1` | Preserves the in-flight logo asset validation update. |
| `icon.svg` | Preserves the untracked source logo asset; it is not folded into Phase 1 behavior. |

## Desktop application

| File | Current-candidate purpose |
| --- | --- |
| `src/Octadock.App/Ai/AgentReviewLaunch.cs` | Defines normalized, single-line source-bound review launch commands and compatibility aliases. |
| `src/Octadock.App/Ai/AgentWorkspaceSupport.cs` | Carries reviewed evidence/source metadata into the workspace. |
| `src/Octadock.App/Ai/AgentWorkspaceViewModel.cs` | Applies source-bound launches, exact Context inclusion, fail-closed reset, and temporary-lease cleanup. |
| `src/Octadock.App/Ai/AgentWorkspaceWindow.xaml` | Presents the reviewed source/evidence state with accessible labels. |
| `src/Octadock.App/Ai/AiActionsSupport.cs` | Routes legacy AI actions through the reviewed workspace contract. |
| `src/Octadock.App/Ai/AiActionsWindow.xaml` | Keeps compatibility UI copy and accessibility aligned with reviewed handoff. |
| `src/Octadock.App/App.xaml.cs` | Initializes active Context state before dependent surfaces. |
| `src/Octadock.App/CaptureUx/CaptureCountdownPill.cs` | Uses semantic resources and accessible countdown state. |
| `src/Octadock.App/CaptureUx/DockPill.cs` | Launches reviewed handoff and uses live dynamic theme resources for persistent chrome/state. |
| `src/Octadock.App/CaptureUx/HudWindow.xaml` | Adds semantic colors and automation names. |
| `src/Octadock.App/CaptureUx/HudWindow.xaml.cs` | Adds keyboard activation for HUD actions. |
| `src/Octadock.App/CaptureUx/RecordingPill.cs` | Uses semantic capture/media tokens and accessibility metadata. |
| `src/Octadock.App/CaptureUx/ScrollingSessionPill.cs` | Uses semantic scrolling-state resources and accessible status. |
| `src/Octadock.App/CaptureUx/SelectionOverlayWindow.xaml` | Replaces raw overlay colors with capture tokens and names interactive controls. |
| `src/Octadock.App/CaptureUx/SelectionOverlayWindow.xaml.cs` | Supplies keyboard activation for selection actions. |
| `src/Octadock.App/CaptureUx/ShelfItemView.xaml` | Adds keyboard-open/accessibility behavior to Shelf rows. |
| `src/Octadock.App/CaptureUx/ShelfItemViewModel.cs` | Preserves exact capture identity and launches the selected Shelf item into review. |
| `src/Octadock.App/CaptureUx/WindowPickerOverlay.xaml` | Uses the dedicated picker-dim token and accessible window choices. |
| `src/Octadock.App/Clipboard/ClipboardHistoryViewModel.cs` | Launches exact text/image clipboard entries into reviewed handoff. |
| `src/Octadock.App/Clipboard/ClipboardHistoryWindow.xaml` | Adds keyboard/window-chrome accessibility alternatives. |
| `src/Octadock.App/Clipboard/ClipboardHistoryWindow.xaml.cs` | Implements keyboard system-menu access for the custom header. |
| `src/Octadock.App/Context/ActiveContextState.cs` | Tracks the explicit active Context destination and user-facing add label. |
| `src/Octadock.App/Context/ContextViewModel.cs` | Implements exact include/export, notes, reorder, validation, and reviewed launch. |
| `src/Octadock.App/Context/ContextWindow.xaml` | Adds include controls, notes/reorder UI, keyboard open, and accessible labeling. |
| `src/Octadock.App/Context/ContextWindow.xaml.cs` | Implements keyboard/window interactions and safe reorder handling. |
| `src/Octadock.App/DependencyInjection/AppServiceCollectionExtensions.cs` | Registers active Context and the testable preview-card host. |
| `src/Octadock.App/Editing/AnnotationEditorWindow.xaml` | Adds semantic resources and keyboard alternatives for annotation actions. |
| `src/Octadock.App/Editing/AnnotationEditorWindow.xaml.cs` | Implements the annotation keyboard alternative. |
| `src/Octadock.App/Editing/EditorCanvas.cs` | Uses the dedicated crop-dim token while preserving original opacity. |
| `src/Octadock.App/Editing/ImageMockupWindow.xaml` | Replaces raw colors and labels mockup controls. |
| `src/Octadock.App/History/CaptureItemViewModel.cs` | Retains exact capture identity for reviewed launch. |
| `src/Octadock.App/History/HistoryViewModel.cs` | Routes History selections through source-bound review. |
| `src/Octadock.App/History/HistoryWindow.xaml` | Adds keyboard opening and accessible capture-row behavior. |
| `src/Octadock.App/Imaging/FrameImaging.cs` | Hardens image decode/frame conversion and bounded preview behavior. |
| `src/Octadock.App/Pins/ImageSaveChoiceDialog.cs` | Fixes accent-button foreground contrast through the semantic accent-text token. |
| `src/Octadock.App/Pins/PinService.cs` | Carries pin source identity and owns/deletes review-only temporary leases. |
| `src/Octadock.App/Pins/PinViewModel.cs` | Exposes exact pin source metadata to review. |
| `src/Octadock.App/Pins/PinWindow.xaml` | Adds accessible pin commands and tokenized chrome. |
| `src/Octadock.App/Pins/PinWindow.xaml.cs` | Launches the current pin into review and preserves keyboard/window behavior. |
| `src/Octadock.App/Preview/FilePreviewService.cs` | Implements latest-request-wins preview recovery, cancellation, retirement, and host separation. |
| `src/Octadock.App/Preview/ImagePreviewProvider.cs` | Uses safe metadata/decode boundaries for image previews. |
| `src/Octadock.App/Preview/PreviewCardHost.cs` | Owns the reusable card seam and refreshes an open card after ThemeManager applies a palette. |
| `src/Octadock.App/Preview/PreviewCardWindow.cs` | Implements bounded/recoverable cards, accessibility, DPI sizing, and live dynamic theme/high-contrast resources. |
| `src/Octadock.App/Preview/PreviewClipboardContent.cs` | Keeps original and explicitly formatted clipboard representations distinct. |
| `src/Octadock.App/Preview/PreviewInspectorModel.cs` | Produces truthful preview metadata/failure rows. |
| `src/Octadock.App/Reading/ReadingPill.cs` | Uses semantic tokens and accessible reading status. |
| `src/Octadock.App/Resources/Themes/Dark.xaml` | Adds semantic capture/editor/media/accessibility tokens for dark mode. |
| `src/Octadock.App/Resources/Themes/HighContrast.xaml` | Maps new semantic tokens to Windows system colors/transparent overlays. |
| `src/Octadock.App/Resources/Themes/Light.xaml` | Adds the matching semantic token set for light mode. |
| `src/Octadock.App/Services/CaptureCoordinator.cs` | Propagates durable capture IDs for all persisted capture paths while preserving existing interface calls. |
| `src/Octadock.App/Services/CommandDispatcher.cs` | Returns truthful cancellation/failure and exact capture IDs to automation. |
| `src/Octadock.App/Services/ContextService.cs` | Adds exact Context inclusion, notes, reorder, and validation orchestration. |
| `src/Octadock.App/Services/WindowPresenter.cs` | Opens one source-bound review and applies contextual owner/topmost policy. |
| `src/Octadock.App/Stt/DictationPill.cs` | Uses semantic dictation-state resources and accessible live status. |
| `src/Octadock.App/TextTools/TextToolsWindow.xaml` | Adds missing automation naming. |
| `src/Octadock.App/Theming/OctadockDesignTokens.cs` | Resolves code-built surfaces from the active WPF resource dictionaries. |
| `src/Octadock.App/Theming/ThemeManager.cs` | Publishes a post-apply event so code-built open surfaces can refresh live. |
| `src/Octadock.App/Tray/TrayIconController.cs` | Routes tray handoff through the same reviewed workspace. |

## Core, data, platform, and CLI

| File | Current-candidate purpose |
| --- | --- |
| `src/Octadock.Cli/CliConsole.cs` | Emits capture IDs in JSON success output while keeping plain-text output stable. |
| `src/Octadock.Cli/HelpText.cs` | Documents JSON capture correlation and cancellation semantics. |
| `src/Octadock.Cli/Program.cs` | Passes the additive IPC response through CLI rendering. |
| `src/Octadock.Core/Abstractions/FilePreviewAbstractions.cs` | Defines preview scope, warning, failure, recovery, and provenance contracts. |
| `src/Octadock.Core/Abstractions/Orchestration.cs` | Adds durable capture identity to command results. |
| `src/Octadock.Core/Context/ContextExport.cs` | Exports only explicitly included, still-valid Context items and fails closed otherwise. |
| `src/Octadock.Core/Context/ContextModels.cs` | Adds package notes, clarifies source provenance, and makes persisted item ordering explicit. |
| `src/Octadock.Core/Context/IContextRepository.cs` | Defines package-note updates and exact item-order persistence operations. |
| `src/Octadock.Core/Ipc/IpcProtocol.cs` | Adds optional protocol-v1 `CaptureId` without breaking legacy responses. |
| `src/Octadock.Core/Services/CsvPreviewProvider.cs` | Hardens bounded CSV/TSV sampling, encoding, diagnostics, and recovery. |
| `src/Octadock.Core/Services/JsonPreviewProvider.cs` | Hardens bounded JSON reading/formatting with explicit original-vs-formatted provenance. |
| `src/Octadock.Core/Services/LogPreviewProvider.cs` | Adds bounded log recovery/diagnostics. |
| `src/Octadock.Core/Services/MarkdownPreviewProvider.cs` | Adds bounded Markdown recovery/diagnostics. |
| `src/Octadock.Core/Services/PreviewTextReader.cs` | Centralizes safe bounded reads, encoding detection, file-change checks, and failure taxonomy. |
| `src/Octadock.Core/Services/TextPreviewProvider.cs` | Applies bounded text preview/recovery contracts. |
| `src/Octadock.Data/Repositories/ContextRepository.cs` | Persists notes and exact item order, rejecting stale or missing-package reorder operations. |
| `src/Octadock.Data/Sqlite/OctadockDatabase.cs` | Applies/salvages the Context schema additions safely. |
| `src/Octadock.Data/Sqlite/SchemaMigrations.cs` | Adds migration 9 for user-authored Context package notes. |
| `src/Octadock.Platform.Windows/System/SingleInstanceGuard.cs` | Carries optional capture IDs across the per-user named-pipe response. |

## Regression coverage

| File | Current-candidate purpose |
| --- | --- |
| `tests/Octadock.App.Tests/Ai/AgentReviewLaunchTests.cs` | Covers normalized source titles, aliases, and long/single-line labels. |
| `tests/Octadock.App.Tests/Ai/AgentWorkspaceViewModelTests.cs` | Covers exact source review, mixed-source fail-closed reset, Context inclusion, and lease cleanup. |
| `tests/Octadock.App.Tests/CaptureUx/ShelfItemViewModelTests.cs` | Covers Shelf identity and reviewed-launch behavior. |
| `tests/Octadock.App.Tests/Context/ActiveContextStateTests.cs` | Covers explicit active-destination state and labels. |
| `tests/Octadock.App.Tests/Context/ContextServiceTests.cs` | Covers Context exact inclusion, notes/reorder, validation, and launch data. |
| `tests/Octadock.App.Tests/History/CaptureItemViewModelTests.cs` | Covers History capture identity propagation. |
| `tests/Octadock.App.Tests/Preview/FilePreviewServiceTests.cs` | Covers cancellation, retirement, stale results, recovery, and reusable host behavior. |
| `tests/Octadock.App.Tests/Preview/PreviewCardWindowTests.cs` | Covers palette, inspector, fit, viewport, and mixed-DPI sizing helpers. |
| `tests/Octadock.App.Tests/Preview/PreviewClipboardContentTests.cs` | Covers original/formatted clipboard truth. |
| `tests/Octadock.App.Tests/Services/CommandLicenseRoutingTests.cs` | Covers capture result IDs, cancellation, and licensed routing. |
| `tests/Octadock.Cli.Tests/CliConsoleTests.cs` | Covers additive JSON `captureId` and stable plain output. |
| `tests/Octadock.Cli.Tests/HelpTextTests.cs` | Covers documented automation semantics. |
| `tests/Octadock.Core.Tests/Context/ContextExporterTests.cs` | Covers exact Context export inclusion/failure behavior. |
| `tests/Octadock.Core.Tests/Ipc/IpcProtocolTests.cs` | Covers capture-ID round trips and legacy response compatibility. |
| `tests/Octadock.Core.Tests/Services/PreviewProviderTests.cs` | Covers provider selection and bounded preview behavior. |
| `tests/Octadock.Core.Tests/Services/PreviewRecoveryTests.cs` | Covers the recovery/failure taxonomy and changed-file safeguards. |
| `tests/Octadock.Data.Tests/CaptureRepositoryTests.cs` | Covers durable capture lookup used by soak correlation. |
| `tests/Octadock.Data.Tests/ContextRepositoryTests.cs` | Covers notes/order/inclusion persistence and missing-package rejection. |
| `tests/Octadock.Data.Tests/DatabaseInitializationTests.cs` | Covers migration 9 and salvage/restart correctness. |

## Integration boundary

This inventory prepares, but does not authorize, a mixed-ownership commit. Before
landing, review the listed preview/context and branding files with their original
author, then commit the accepted candidate without stashing, reverting, or
silently dropping any path. Rendered/manual and founder-blocked acceptance remains
tracked in `docs/ROADMAP.md`.
