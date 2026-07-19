using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Commands;
using Octadock.Core.Context;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Octadock.Core.Licensing;
using Octadock.Core.Persistence;

namespace Octadock.App.Ai;

public sealed record AgentCaptureChoice(CaptureRecord Record)
{
    public string Label => Record.Source.ProcessName is { Length: > 0 } application
        ? $"{Record.CreatedAt.ToLocalTime():MMM d, HH:mm}  ·  {application}"
        : $"{Record.CreatedAt.ToLocalTime():MMM d, HH:mm}  ·  {Record.Type}";
}

public sealed record AgentContextChoice(ContextPackage Package)
{
    public string Label => $"{Package.Name}  ·  {Package.Items.Count} item{(Package.Items.Count == 1 ? string.Empty : "s")}";
}

/// <summary>
/// Builds evidence-rich, privacy-reviewed tasks for a real coding/reasoning
/// agent. This replaces the visible commodity text-action surface while the old
/// command token and services remain available as compatibility adapters.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class AgentWorkspaceViewModel : ObservableObject, IDisposable
{
    private const string DefaultCriteria =
        "The requested outcome is completed using the supplied evidence.\n" +
        "Existing unrelated behavior remains unchanged.\n" +
        "Each required criterion is reported as passed, failed, or not verified with concrete evidence.";
    private const string VisualVerificationCriterion =
        "Any visual changes are intentional; unintended regressions are absent or explicitly reported.";

    private readonly IAgentPacketBuilder _packetBuilder;
    private readonly AgentEvidenceFactory _evidenceFactory;
    private readonly ICaptureRepository _captures;
    private readonly ContextService _context;
    private readonly IClipboardService _clipboard;
    private readonly IOcrService _ocr;
    private readonly IVisualComparisonService _visualComparison;
    private readonly IAgentWorkspacePicker _picker;
    private readonly IAgentPacketExportService _exports;
    private readonly IAgentCliRunner _agents;
    private readonly IAgentHandoffConfirmation _confirmation;
    private readonly IAgentTemporaryLeaseStore _temporaryLeases;
    private readonly IStoragePaths _paths;
    private readonly ICommandDispatcher _commands;
    private readonly ILicenseGate _licenseGate;
    private readonly ILogger<AgentWorkspaceViewModel> _logger;

    private string _packetId = $"task-{Guid.NewGuid():N}";
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;
    private ReviewedAgentPacket? _currentReview;
    private string? _resultReviewSha256;
    private string? _resultProviderName;
    private string? _lastExportReviewSha256;
    private CancellationTokenSource? _operationCancellation;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly SemaphoreSlim _draftMutationGate = new(1, 1);
    private readonly object _launchSync = new();
    private CancellationTokenSource? _launchCancellation;
    private long _launchVersion;
    private bool _syncingVerificationSelection;
    private bool _disposed;
    private string _workflowBaseGoal = string.Empty;

    [ObservableProperty]
    private string _goal = string.Empty;

    [ObservableProperty]
    private string _additionalIntent = string.Empty;

    [ObservableProperty]
    private string _taskTitle = "Untitled agent task";

    [ObservableProperty]
    private string _acceptanceCriteriaText = DefaultCriteria;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _targetApplication = string.Empty;

    [ObservableProperty]
    private string _environment = string.Empty;

    [ObservableProperty]
    private bool _redactSecrets = true;

    [ObservableProperty]
    private AgentWorkspaceEvidence? _selectedEvidence;

    [ObservableProperty]
    private AgentCaptureChoice? _selectedRecentCapture;

    [ObservableProperty]
    private AgentContextChoice? _selectedContext;

    [ObservableProperty]
    private AgentWorkflowDefinition? _selectedWorkflow;

    [ObservableProperty]
    private AiProviderOption _selectedProvider;

    [ObservableProperty]
    private string _packetPreview = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Start from a capture, pin, Context, clipboard item, voice note, or choose an outcome here.";

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _detectedSecretCount;

    [ObservableProperty]
    private string _lastExportedDirectory = string.Empty;

    [ObservableProperty]
    private string _verificationSummary = string.Empty;

    [ObservableProperty]
    private int _selectedWorkspaceTabIndex;

    [ObservableProperty]
    private string _reviewHeading = "Review handoff";

    [ObservableProperty]
    private string _reviewSourceLabel = "Octadock · New reviewed handoff";

    [ObservableProperty]
    private string _reviewWindowTitle = "Octadock · Review handoff";

    public AgentWorkspaceViewModel(
        IAgentPacketBuilder packetBuilder,
        AgentEvidenceFactory evidenceFactory,
        ICaptureRepository captures,
        ContextService context,
        IClipboardService clipboard,
        IOcrService ocr,
        IVisualComparisonService visualComparison,
        IAgentWorkspacePicker picker,
        IAgentPacketExportService exports,
        IAgentCliRunner agents,
        IAgentHandoffConfirmation confirmation,
        IAgentTemporaryLeaseStore temporaryLeases,
        IStoragePaths paths,
        ICommandDispatcher commands,
        ILicenseGate licenseGate,
        ILogger<AgentWorkspaceViewModel> logger)
    {
        _packetBuilder = packetBuilder ?? throw new ArgumentNullException(nameof(packetBuilder));
        _evidenceFactory = evidenceFactory ?? throw new ArgumentNullException(nameof(evidenceFactory));
        _captures = captures ?? throw new ArgumentNullException(nameof(captures));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _visualComparison = visualComparison ?? throw new ArgumentNullException(nameof(visualComparison));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _exports = exports ?? throw new ArgumentNullException(nameof(exports));
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _temporaryLeases = temporaryLeases ?? throw new ArgumentNullException(nameof(temporaryLeases));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _licenseGate = licenseGate ?? throw new ArgumentNullException(nameof(licenseGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Providers = _agents.Providers.Select(item => new AiProviderOption(item)).ToList();
        if (Providers.Count == 0)
        {
            throw new InvalidOperationException("No supported agent CLI destinations are configured.");
        }

        _selectedProvider = Providers.FirstOrDefault(item => item.Descriptor.IsAvailable) ?? Providers[0];
        Evidence.CollectionChanged += OnEvidenceCollectionChanged;
        RefreshReview();
    }

    public ObservableCollection<AgentWorkspaceEvidence> Evidence { get; } = [];

    public ObservableCollection<AgentCaptureChoice> RecentCaptures { get; } = [];

    public ObservableCollection<AgentContextChoice> ContextPackages { get; } = [];

    public IReadOnlyList<AgentWorkflowDefinition> Workflows => AgentWorkflowCatalog.All;

    public IReadOnlyList<AiProviderOption> Providers { get; }

    public bool IsNotBusy => !IsBusy;

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    public bool HasExport => !string.IsNullOrWhiteSpace(LastExportedDirectory);

    public bool HasReview => _currentReview is not null;

    public bool HasEvidence => Evidence.Any(item => item.IsIncluded);

    public bool NeedsEvidence => !HasEvidence;

    public bool NeedsOutcome => HasEvidence && !HasSelectedWorkflow;

    public bool HasSelectedWorkflow => SelectedWorkflow is not null;

    public string WorkflowHeading => SelectedWorkflow?.Title ?? "What should happen next?";

    public string WorkflowDescription => SelectedWorkflow?.Description
        ?? "Choose a useful outcome. Octadock will carry the evidence and ask only for missing intent.";

    public string GuideTitle => !HasEvidence
        ? "Start with something AI can see"
        : !HasSelectedWorkflow
            ? "Now choose the result you want"
            : HasReview
                ? "Ready to run"
                : "Add anything the evidence does not explain";

    public string GuideDescription => !HasEvidence
        ? "Add the current screenshot, clipboard, Context, or a file. The fastest path is to press Use with AI on the item itself; Octadock attaches it automatically."
        : !HasSelectedWorkflow
            ? "Your evidence is attached. Pick Build, Investigate, Verify, Extract, or Handoff—Octadock will prepare the right instructions for that job."
            : HasReview
                ? "The evidence and outcome are ready. Add optional context on the left, review what will be used, then run the workflow."
                : "Say only what is missing. You can type or dictate it; Octadock keeps the attached evidence and definition of done.";

    public string GuideStepLabel => !HasEvidence
        ? "STEP 1 OF 3 · ADD EVIDENCE"
        : !HasSelectedWorkflow
            ? "STEP 2 OF 3 · CHOOSE OUTCOME"
            : "STEP 3 OF 3 · REVIEW AND RUN";

    public bool HasDetectedSecrets => DetectedSecretCount > 0;

    public bool IncludesPixelEvidence => Evidence.Any(item => item.IsIncluded && item.HasPixels);

    public bool IncludesUnscannedAttachments =>
        Evidence.Any(item => item.IsIncluded && item.RelativeAssetPath is not null);

    public string PrivacyLabel
    {
        get
        {
            string textReview = DetectedSecretCount switch
            {
                0 => "No common secret patterns detected in included text.",
                _ when RedactSecrets => $"{DetectedSecretCount:N0} common secret pattern(s) redacted from included text.",
                _ => $"{DetectedSecretCount:N0} common secret pattern(s) remain in included text.",
            };
            string attachmentReview = IncludesPixelEvidence
                ? " Image pixels are attached unchanged and are not scanned or redacted."
                : IncludesUnscannedAttachments
                    ? " Binary attachments are included unchanged and are not text-scanned or redacted."
                    : string.Empty;
            return textReview + attachmentReview;
        }
    }

    public string EvidenceSummary
    {
        get
        {
            int included = Evidence.Count(item => item.IsIncluded);
            long bytes = Evidence.Where(item => item.IsIncluded).Sum(item => item.SizeBytes);
            return $"{included:N0} included  ·  {FormatBytes(bytes)}  ·  {PacketPreview.Length:N0} reviewed chars";
        }
    }

    public string EvidenceStepSummary
    {
        get
        {
            int included = Evidence.Count(item => item.IsIncluded);
            long bytes = Evidence.Where(item => item.IsIncluded).Sum(item => item.SizeBytes);
            string itemLabel = included == 1 ? "1 item attached" : $"{included:N0} items attached";
            return $"{itemLabel}  ·  {FormatBytes(bytes)}";
        }
    }

    public string DestinationLabel =>
        $"{SelectedProvider.Descriptor.DisplayName} CLI  ·  read-only  ·  no provider fallback";

    public string AnalyzeButtonText => !HasEvidence
        ? "Add evidence first"
        : SelectedWorkflow is null
            ? "Choose an outcome"
            : $"Run · {SelectedWorkflow.Title}";

    public string ReviewHash => _currentReview is null
        ? string.Empty
        : $"SHA-256  {_currentReview.OutboundSha256[..12]}…";

    public bool IsResultStale =>
        HasResult &&
        (_currentReview is null ||
         !string.Equals(_resultReviewSha256, _currentReview.OutboundSha256, StringComparison.Ordinal));

    public string ResultIntegrityLabel
    {
        get
        {
            if (!HasResult || string.IsNullOrWhiteSpace(_resultReviewSha256))
            {
                return "No agent review yet.";
            }

            string provider = string.IsNullOrWhiteSpace(_resultProviderName) ? "Agent" : _resultProviderName;
            string hash = _resultReviewSha256[..Math.Min(12, _resultReviewSha256.Length)];
            return IsResultStale
                ? $"Draft changed since this {provider} review · SHA-256 {hash}…"
                : $"{provider} review for SHA-256 {hash}…";
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        CancellationToken token = linkedCancellation.Token;
        try
        {
            Task<IReadOnlyList<CaptureRecord>> captures = _captures.GetRecentAsync(12, token);
            Task<IReadOnlyList<ContextPackage>> packages = _context.GetPackagesAsync(token);
            await Task.WhenAll(captures, packages).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();

            RecentCaptures.Clear();
            foreach (CaptureRecord capture in captures.Result.Where(item => !item.IsRecording))
            {
                RecentCaptures.Add(new AgentCaptureChoice(capture));
            }

            SelectedRecentCapture = RecentCaptures.FirstOrDefault();
            ContextPackages.Clear();
            foreach (ContextPackage package in packages.Result)
            {
                ContextPackages.Add(new AgentContextChoice(package));
            }

            SelectedContext = ContextPackages.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent Workspace source catalog could not be loaded.");
            SetError("Recent captures or Context packages could not be loaded.");
        }
    }

    /// <summary>Translates the stable legacy AI command into an Agent Workspace draft.</summary>
    public async Task ApplyLaunchCommandAsync(
        OctadockCommand? command,
        CancellationToken cancellationToken = default)
    {
        long version = Interlocked.Increment(ref _launchVersion);
        if (_disposed)
        {
            return;
        }

        if (command is null)
        {
            await ResetToHomeAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        string? temporaryLeaseToken = command.Get("templease");

        if (IsBusy)
        {
            if (!string.IsNullOrWhiteSpace(temporaryLeaseToken))
            {
                _temporaryLeases.Release(temporaryLeaseToken);
            }

            StatusText = "Finish or cancel the current handoff review before loading another task.";
            return;
        }

        var launchCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        lock (_launchSync)
        {
            _launchCancellation?.Cancel();
            _launchCancellation = launchCancellation;
        }
        CancellationToken token = launchCancellation.Token;
        bool enteredGate = false;
        bool enteredDraftGate = false;
        bool leaseClaimed = false;
        bool leaseTransferred = false;
        try
        {
            await _launchGate.WaitAsync(token).ConfigureAwait(true);
            enteredGate = true;
            await _draftMutationGate.WaitAsync(token).ConfigureAwait(true);
            enteredDraftGate = true;
            token.ThrowIfCancellationRequested();
            ResetDraftForLaunch();
            ApplyReviewSource(command);
            string? provider = command.Get("provider");
            AiProviderOption? providerOption = Providers.FirstOrDefault(item =>
                string.Equals(item.Descriptor.Id, provider, StringComparison.OrdinalIgnoreCase));
            if (providerOption is not null)
            {
                SelectedProvider = providerOption;
            }

            string? explicitGoal = command.Get("goal");
            string? workflowKey = command.Get("workflow");
            string? legacyAction = command.Get("action");
            bool chooseOutcome = string.Equals(workflowKey, "choose", StringComparison.OrdinalIgnoreCase);
            AgentWorkflowDefinition? workflow = AgentWorkflowCatalog.Resolve(workflowKey)
                ?? AgentWorkflowCatalog.Resolve(legacyAction);
            bool hasExplicitEvidence =
                !string.IsNullOrWhiteSpace(command.Get("text")) ||
                !string.IsNullOrWhiteSpace(command.FilePath) ||
                command.GetBool("clipboard") ||
                !string.IsNullOrWhiteSpace(command.Get("captureid")) ||
                !string.IsNullOrWhiteSpace(command.Get("contextid"));
            if (workflow is null &&
                !chooseOutcome &&
                string.IsNullOrWhiteSpace(explicitGoal) &&
                string.IsNullOrWhiteSpace(legacyAction) &&
                hasExplicitEvidence)
            {
                // Compatibility for `octadock ai --text/--filepath/...`: an evidence-only
                // command historically meant investigate. Blank workspace launches remain
                // truly blank, while current in-app entry points explicitly pass `choose`.
                workflow = AgentWorkflowCatalog.Resolve(AgentWorkflowCatalog.InvestigateKey);
            }

            if (workflow is not null)
            {
                ApplyWorkflow(workflow, replaceIntent: true);
            }

            if (!string.IsNullOrWhiteSpace(explicitGoal))
            {
                if (workflow is not null)
                {
                    AdditionalIntent = explicitGoal.Trim();
                }
                else
                {
                    Goal = explicitGoal;
                    AcceptanceCriteriaText = DefaultCriteria;
                }
            }
            else if (!chooseOutcome &&
                     workflow is null &&
                     !string.IsNullOrWhiteSpace(legacyAction) &&
                     string.IsNullOrWhiteSpace(Goal))
            {
                Goal = LegacyGoal(legacyAction);
                AcceptanceCriteriaText = DefaultCriteria;
            }

            if (!string.IsNullOrWhiteSpace(command.Title))
            {
                TaskTitle = command.Title.Trim();
            }

            if (!string.IsNullOrWhiteSpace(command.Get("project")))
            {
                ProjectName = command.Get("project")!.Trim();
            }

            if (!string.IsNullOrWhiteSpace(command.Get("target")))
            {
                TargetApplication = command.Get("target")!.Trim();
            }

            if (!string.IsNullOrWhiteSpace(command.Get("environment")))
            {
                Environment = command.Get("environment")!.Trim();
            }

            if (!string.IsNullOrWhiteSpace(command.Get("criteria")))
            {
                AcceptanceCriteriaText = command.Get("criteria")!
                    .Replace("|", System.Environment.NewLine, StringComparison.Ordinal);
            }

            if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
            {
                ProjectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(command.WorkingDirectory));
                Environment = "Workspace supplied by automation; this review remains analyze-only.";
            }

            if (command.GetBool("clipboard"))
            {
                await AddClipboardCoreAsync(token).ConfigureAwait(true);
            }

            string? suppliedText = command.Get("text");
            if (!string.IsNullOrWhiteSpace(suppliedText))
            {
                AddEvidence(_evidenceFactory.CreateText(
                    string.IsNullOrWhiteSpace(command.Get("source"))
                        ? "Automation text"
                        : command.Get("source")!,
                    suppliedText,
                    AgentPacketProvenanceKind.UserProvided));
            }

            if (!string.IsNullOrWhiteSpace(command.FilePath))
            {
                if (!string.IsNullOrWhiteSpace(temporaryLeaseToken))
                {
                    leaseClaimed = _temporaryLeases.TryClaim(temporaryLeaseToken, command.FilePath);
                    if (!leaseClaimed)
                    {
                        throw new InvalidOperationException("The composed pin evidence lease expired before the handoff review could claim it.");
                    }
                }

                AgentPacketProvenance? fileProvenance = command.Get(AgentReviewLaunch.ReviewSourceParameter)
                    ?.Trim().ToLowerInvariant() switch
                    {
                        "clipboard" => new AgentPacketProvenance { Kind = AgentPacketProvenanceKind.Clipboard },
                        "pin" => new AgentPacketProvenance { Kind = AgentPacketProvenanceKind.UserProvided },
                        _ => null,
                    };
                await AddPathCoreAsync(
                        command.FilePath,
                        token,
                        leaseClaimed,
                        command.Get("source"),
                        fileProvenance)
                    .ConfigureAwait(true);
                leaseTransferred = leaseClaimed;
            }

            if (command.Has("captureid"))
            {
                if (!Guid.TryParse(command.Get("captureid"), out Guid captureId))
                {
                    throw new ArgumentException("captureid must be a valid GUID.");
                }

                CaptureRecord? capture = await _captures.GetAsync(captureId, token).ConfigureAwait(true);
                if (capture is null)
                {
                    throw new InvalidOperationException("The requested capture no longer exists.");
                }

                await AddEvidenceAsync(_evidenceFactory.FromCaptureAsync(capture, token)).ConfigureAwait(true);
            }

            if (command.Has("contextid"))
            {
                if (!Guid.TryParse(command.Get("contextid"), out Guid contextId))
                {
                    throw new ArgumentException("contextid must be a valid GUID.");
                }

                ContextPackage? package = await _context.GetPackageAsync(contextId, token).ConfigureAwait(true);
                if (package is null)
                {
                    throw new InvalidOperationException("The requested Context package no longer exists.");
                }

                HashSet<Guid>? includedContextItems = null;
                if (command.Has("contextitems"))
                {
                    includedContextItems = [];
                    foreach (string value in (command.Get("contextitems") ?? string.Empty)
                                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!Guid.TryParse(value, out Guid itemId))
                        {
                            throw new ArgumentException("contextitems must contain only comma-separated GUIDs.");
                        }

                        includedContextItems.Add(itemId);
                    }

                    if (includedContextItems.Count == 0)
                    {
                        throw new InvalidOperationException("The Context handoff must include at least one item.");
                    }
                }

                await AddContextCoreAsync(package, token, includedContextItems).ConfigureAwait(true);
            }

            if (version == Volatile.Read(ref _launchVersion))
            {
                StatusText = BuildLaunchStatus(workflow);
            }
        }
        catch (OperationCanceledException)
        {
            if (enteredDraftGate)
            {
                ResetDraftForLaunch();
                StatusText = "The source launch was cancelled before a reviewed handoff was created.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent Workspace launch command could not be applied.");
            if (enteredDraftGate)
            {
                // A launch can carry several independent sources. If any later
                // source fails validation, discard every earlier staged source;
                // a partial packet must never remain copyable or runnable.
                ResetDraftForLaunch();
            }
            SetError(ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryLeaseToken) && !leaseClaimed)
            {
                _temporaryLeases.Release(temporaryLeaseToken);
            }
            else if (leaseClaimed && !leaseTransferred && !string.IsNullOrWhiteSpace(command.FilePath))
            {
                DeleteOwnedTemporaryPath(command.FilePath);
            }

            if (enteredDraftGate)
            {
                _draftMutationGate.Release();
            }

            if (enteredGate)
            {
                _launchGate.Release();
            }

            lock (_launchSync)
            {
                if (ReferenceEquals(_launchCancellation, launchCancellation))
                {
                    _launchCancellation = null;
                }
            }

            launchCancellation.Dispose();
        }
    }

    partial void OnGoalChanged(string value)
    {
        if (TaskTitle == "Untitled agent task" || string.IsNullOrWhiteSpace(TaskTitle))
        {
            string firstLine = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim()
                ?? "Untitled agent task";
            TaskTitle = firstLine.Length <= 90 ? firstLine : firstLine[..90];
        }

        RefreshReview();
    }

    partial void OnAdditionalIntentChanged(string value)
    {
        if (SelectedWorkflow is null || string.IsNullOrWhiteSpace(_workflowBaseGoal))
        {
            return;
        }

        Goal = string.IsNullOrWhiteSpace(value)
            ? _workflowBaseGoal
            : $"{_workflowBaseGoal}{System.Environment.NewLine}{System.Environment.NewLine}" +
              $"Additional context from the user:{System.Environment.NewLine}{value.Trim()}";
    }

    partial void OnTaskTitleChanged(string value) => RefreshReview();

    partial void OnAcceptanceCriteriaTextChanged(string value) => RefreshReview();

    partial void OnProjectNameChanged(string value) => RefreshReview();

    partial void OnTargetApplicationChanged(string value) => RefreshReview();

    partial void OnEnvironmentChanged(string value) => RefreshReview();

    partial void OnRedactSecretsChanged(bool value) => RefreshReview();

    partial void OnSelectedProviderChanged(AiProviderOption value)
    {
        OnPropertyChanged(nameof(DestinationLabel));
        OnPropertyChanged(nameof(AnalyzeButtonText));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedWorkflowChanged(AgentWorkflowDefinition? value)
    {
        OnPropertyChanged(nameof(HasSelectedWorkflow));
        OnPropertyChanged(nameof(NeedsOutcome));
        OnPropertyChanged(nameof(WorkflowHeading));
        OnPropertyChanged(nameof(WorkflowDescription));
        OnPropertyChanged(nameof(GuideTitle));
        OnPropertyChanged(nameof(GuideDescription));
        OnPropertyChanged(nameof(GuideStepLabel));
        OnPropertyChanged(nameof(AnalyzeButtonText));
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    private bool CanChooseWorkflow() => !IsBusy && HasEvidence;

    [RelayCommand(CanExecute = nameof(CanChooseWorkflow))]
    private void ChooseWorkflow(AgentWorkflowDefinition? workflow)
    {
        if (workflow is null || IsBusy)
        {
            return;
        }

        ApplyWorkflow(workflow, replaceIntent: true);
        StatusText = workflow.IsVisualVerification && Evidence.Count(item => item.HasPixels) < 2
            ? "Verification selected. Add a result image with Before / after, then review the comparison."
            : $"{workflow.Title} is ready. Refine the intent only if Octadock is missing something.";
    }

    partial void OnResultTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(IsResultStale));
        OnPropertyChanged(nameof(ResultIntegrityLabel));
        CopyResultCommand.NotifyCanExecuteChanged();
        ReadResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedEvidenceChanged(AgentWorkspaceEvidence? value)
    {
        RunOcrCommand.NotifyCanExecuteChanged();
        RemoveSelectedEvidenceCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastExportedDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(HasExport));
        OpenExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        ChooseWorkflowCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        ExportPacketCommand.NotifyCanExecuteChanged();
        CopyPacketCommand.NotifyCanExecuteChanged();
        CancelOperationCommand.NotifyCanExecuteChanged();
        RunOcrCommand.NotifyCanExecuteChanged();
        RemoveSelectedEvidenceCommand.NotifyCanExecuteChanged();
        CompareImagesCommand.NotifyCanExecuteChanged();
        CopyResultCommand.NotifyCanExecuteChanged();
        ReadResultCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        if (IsBusy || _disposed)
        {
            return;
        }

        bool enteredGate = false;
        try
        {
            await _draftMutationGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            enteredGate = true;
            if (IsBusy || _disposed)
            {
                return;
            }

            foreach (string path in _picker.PickEvidenceFiles())
            {
                try
                {
                    await AddPathCoreAsync(path, _lifetimeCancellation.Token).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                {
                    SetError($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (enteredGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    /// <summary>Adds files dropped onto the window through the same bounded evidence path.</summary>
    public async Task AddDroppedFilesAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (IsBusy || _disposed)
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        bool enteredGate = false;
        try
        {
            await _draftMutationGate.WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
            enteredGate = true;
            if (IsBusy || _disposed)
            {
                return;
            }

            foreach (string path in paths)
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    await AddPathCoreAsync(path, linkedCancellation.Token).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                {
                    SetError($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (enteredGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    [RelayCommand]
    private async Task AddClipboardAsync()
    {
        if (IsBusy || _disposed)
        {
            return;
        }

        bool enteredGate = false;
        try
        {
            await _draftMutationGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            enteredGate = true;
            if (IsBusy || _disposed)
            {
                return;
            }

            await AddClipboardCoreAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && _disposed)
            {
                return;
            }

            _logger.LogDebug(ex, "Clipboard evidence could not be read.");
            SetError("The clipboard could not be read. Copy the item again and retry.");
        }
        finally
        {
            if (enteredGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    [RelayCommand]
    private async Task AddRecentCaptureAsync()
    {
        if (IsBusy || _disposed)
        {
            return;
        }

        if (SelectedRecentCapture is null)
        {
            SetError("Choose a recent capture first.");
            return;
        }

        bool enteredGate = false;
        try
        {
            await _draftMutationGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            enteredGate = true;
            if (IsBusy || _disposed)
            {
                return;
            }

            await AddEvidenceAsync(_evidenceFactory.FromCaptureAsync(
                SelectedRecentCapture.Record,
                _lifetimeCancellation.Token)).ConfigureAwait(true);
            StatusText = "Capture added with application, window, dimensions, content hash, and timestamp provenance.";
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && _disposed)
            {
                return;
            }

            SetError(ex.Message);
        }
        finally
        {
            if (enteredGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    [RelayCommand]
    private async Task AddContextAsync()
    {
        if (IsBusy || _disposed)
        {
            return;
        }

        if (SelectedContext is null)
        {
            SetError("Choose a Context package first.");
            return;
        }

        bool enteredGate = false;
        try
        {
            await _draftMutationGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            enteredGate = true;
            if (IsBusy || _disposed)
            {
                return;
            }

            ContextPackage? latest = await _context
                .GetPackageAsync(SelectedContext.Package.Id, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            if (latest is null)
            {
                throw new InvalidOperationException("That Context package no longer exists. Reopen the handoff review to refresh the list.");
            }

            await AddContextCoreAsync(latest, _lifetimeCancellation.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && _disposed)
            {
                return;
            }

            SetError(ex.Message);
        }
        finally
        {
            if (enteredGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    private bool CanRunOcr() => !IsBusy && SelectedEvidence?.CanRunOcr == true;

    [RelayCommand(CanExecute = nameof(CanRunOcr))]
    private async Task RunOcrAsync()
    {
        AgentWorkspaceEvidence? selected = SelectedEvidence;
        if (selected?.LocalPath is null)
        {
            return;
        }

        await RunBusyAsync("Reading visible text locally…", async cancellationToken =>
        {
            string text = await _ocr.ExtractFileTextAsync(
                selected.LocalPath,
                OcrTextMode.Lines,
                language: null,
                cancellationToken).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("No readable text was found in that image.");
            }

            selected.TextContent = text;
            RefreshReview();
            StatusText = $"Local OCR added {text.Length:N0} characters to '{selected.Label}'.";
        }).ConfigureAwait(true);
    }

    private bool CanCompareImages() => !IsBusy && !_disposed;

    [RelayCommand(CanExecute = nameof(CanCompareImages))]
    private async Task CompareImagesAsync()
    {
        bool enteredGate = false;
        try
        {
            await _draftMutationGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            enteredGate = true;
            if (IsBusy || _disposed)
            {
                return;
            }

        int nonVerificationCount = Evidence.Count(item =>
            item.Provenance.Kind != AgentPacketProvenanceKind.VisualVerification);
        if (nonVerificationCount > AgentPacketLimits.MaxSources - 3)
        {
            SetError("Remove evidence before adding a three-image visual verification set.");
            return;
        }

        string? before = _picker.PickImage("Choose the baseline screenshot");
        if (before is null)
        {
            return;
        }

        string? after = _picker.PickImage("Choose the result screenshot");
        if (after is null)
        {
            return;
        }

        await RunBusyAsync("Comparing screenshots locally…", async cancellationToken =>
        {
            VisualComparisonResult? comparison = null;
            var staged = new List<AgentWorkspaceEvidence>(3);
            try
            {
                comparison = await _visualComparison
                    .CompareAsync(before, after, new VisualComparisonOptions { ChannelDeltaThreshold = 8 }, cancellationToken)
                    .ConfigureAwait(true);
                string verificationReference = $"verification:{Guid.NewGuid():N}";
                AgentPacketProvenance Provenance() => new()
                {
                    Kind = AgentPacketProvenanceKind.VisualVerification,
                    Reference = verificationReference,
                };
                AgentWorkspaceEvidence baseline = await _evidenceFactory.FromFileAsync(
                    before,
                    AgentPacketSourceKind.Image,
                    "Verification baseline",
                    Provenance(),
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                staged.Add(baseline);
                AgentWorkspaceEvidence candidate = await _evidenceFactory.FromFileAsync(
                    after,
                    AgentPacketSourceKind.Image,
                    "Verification candidate",
                    Provenance(),
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                staged.Add(candidate);
                AgentWorkspaceEvidence diff = await _evidenceFactory.FromFileAsync(
                    comparison.DiffImagePath,
                    AgentPacketSourceKind.Image,
                    "Visual difference heat map",
                    Provenance(),
                    ownedTemporary: true,
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                diff.TextContent = comparison.MarkdownSummary;
                staged.Add(diff);

                if (!string.Equals(baseline.Sha256, comparison.BaselineSha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(candidate.Sha256, comparison.CandidateSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "A verification image changed during comparison. Choose the stable before and after images again.");
                }

                List<AgentWorkspaceEvidence> previousVerification = Evidence
                    .Where(item => item.Provenance.Kind == AgentPacketProvenanceKind.VisualVerification)
                    .ToList();
                List<AgentWorkspaceEvidence> kept = Evidence.Except(previousVerification).ToList();
                long keptBytes = kept.Where(item => item.RelativeAssetPath is not null).Sum(item => item.SizeBytes);
                long stagedBytes = staged.Where(item => item.RelativeAssetPath is not null).Sum(item => item.SizeBytes);
                if (kept.Count > AgentPacketLimits.MaxSources - staged.Count ||
                    keptBytes > AgentEvidenceFactory.MaxPacketAttachmentBytes - stagedBytes)
                {
                    throw new InvalidOperationException(
                        "Remove evidence before adding this three-image visual verification set.");
                }

                foreach (AgentWorkspaceEvidence previous in previousVerification)
                {
                    Evidence.Remove(previous);
                    DeleteOwnedEvidence(previous);
                }

                foreach (AgentWorkspaceEvidence item in staged)
                {
                    AddEvidence(item);
                }

                staged.Clear();
                SelectedEvidence = diff;
                VerificationSummary = comparison.ExactMatch
                    ? "Exact pixel match"
                    : $"{comparison.ChangedPixelRatio:P2} changed  ·  max delta {comparison.MaxChannelDelta}";
                if (!AcceptanceCriteriaText.Contains("visual", StringComparison.OrdinalIgnoreCase))
                {
                    AcceptanceCriteriaText += "\n" + VisualVerificationCriterion;
                }

                SelectedWorkspaceTabIndex = 3;
                StatusText = "Before, after, heat map, and deterministic change metrics were added to the packet.";
            }
            finally
            {
                foreach (AgentWorkspaceEvidence item in staged)
                {
                    DeleteOwnedEvidence(item);
                }

                if (comparison is not null &&
                    !Evidence.Any(item => item.IsOwnedTemporary && PathsEqual(item.LocalPath, comparison.DiffImagePath)))
                {
                    DeleteOwnedTemporaryPath(comparison.DiffImagePath);
                }
            }
        }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (enteredGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    private bool CanRemoveSelectedEvidence() => !IsBusy && SelectedEvidence is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedEvidence))]
    private void RemoveSelectedEvidence()
    {
        if (SelectedEvidence is not { } selected)
        {
            return;
        }

        int index = Evidence.IndexOf(selected);
        List<AgentWorkspaceEvidence> removing = selected.Provenance.Kind == AgentPacketProvenanceKind.VisualVerification
            ? Evidence.Where(item =>
                    item.Provenance.Kind == AgentPacketProvenanceKind.VisualVerification &&
                    string.Equals(
                        item.Provenance.Reference,
                        selected.Provenance.Reference,
                        StringComparison.Ordinal))
                .ToList()
            : [selected];
        foreach (AgentWorkspaceEvidence item in removing)
        {
            Evidence.Remove(item);
            DeleteOwnedEvidence(item);
        }

        if (selected.Provenance.Kind == AgentPacketProvenanceKind.VisualVerification)
        {
            VerificationSummary = string.Empty;
            AcceptanceCriteriaText = string.Join(
                System.Environment.NewLine,
                AcceptanceCriteriaText
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(line => !string.Equals(line, VisualVerificationCriterion, StringComparison.Ordinal)));
        }
        SelectedEvidence = Evidence.ElementAtOrDefault(Math.Clamp(index, 0, Math.Max(0, Evidence.Count - 1)));
        StatusText = "Evidence removed from this draft. No source file was changed.";
    }

    private bool CanUseReview() => !IsBusy && !HasError && _currentReview is not null;

    [RelayCommand(CanExecute = nameof(CanUseReview))]
    private void CopyPacket()
    {
        if (_currentReview is null)
        {
            return;
        }

        try
        {
            _clipboard.SetText(_currentReview.OutboundMarkdown);
            StatusText = "Reviewed TASK.md copied. Attach the included image files when pasting into a chat client.";
        }
        catch (Exception)
        {
            SetError("The reviewed packet could not be copied.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseReview))]
    private async Task ExportPacketAsync()
    {
        string? prePickerReviewSha256 = _currentReview?.OutboundSha256;
        string? parent = _picker.PickExportFolder();
        if (parent is null)
        {
            return;
        }

        bool enteredGate = false;
        try
        {
        await _draftMutationGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
        enteredGate = true;
        ReviewedAgentPacket? review = _currentReview;
        if (review is null || !string.Equals(
                prePickerReviewSha256,
                review.OutboundSha256,
                StringComparison.Ordinal))
        {
            SetError("The draft changed while the destination picker was open. Review it before saving.");
            return;
        }

        List<AgentWorkspaceEvidence> included = Evidence.Where(item => item.IsIncluded).ToList();
        await RunBusyAsync("Materializing the reviewed packet…", async cancellationToken =>
        {
            if (!string.Equals(_currentReview?.OutboundSha256, review.OutboundSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The draft changed while the bundle was being prepared. Review it again before saving.");
            }

            AgentPacketBundle bundle = await _exports.ExportAsync(
                review,
                included,
                parent,
                cancellationToken).ConfigureAwait(true);
            LastExportedDirectory = bundle.DirectoryPath;
            _lastExportReviewSha256 = review.OutboundSha256;
            StatusText = $"Saved a self-contained packet with TASK.md, manifest.json, hashes, and {bundle.ImagePaths.Count:N0} image(s).";
        }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (enteredGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    private bool CanAnalyze() =>
        CanUseReview() && SelectedProvider.Descriptor.IsAvailable;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (!_licenseGate.Allow(GatedFeature.AiActions))
        {
            StatusText = "Agent handoff needs an active trial or license. Your reviewed draft remains local.";
            return;
        }

        bool enteredDraftGate = false;
        try
        {
            await _draftMutationGate.WaitAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            enteredDraftGate = true;
            ReviewedAgentPacket? review = _currentReview;
            if (review is null)
            {
                return;
            }

            AiCliProviderDescriptor provider = SelectedProvider.Descriptor;
            AgentPacketBundle? bundle = null;
            try
            {
            await RunBusyAsync($"Preparing the reviewed packet for {provider.DisplayName}…", async cancellationToken =>
            {
                List<AgentWorkspaceEvidence> included = Evidence.Where(item => item.IsIncluded).ToList();
                if (!string.Equals(_currentReview?.OutboundSha256, review.OutboundSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The draft changed while the handoff packet was being prepared. Review it again before sending.");
                }

                if (string.Equals(provider.Id, AiCliProviderIds.Codex, StringComparison.Ordinal) &&
                    included.Any(item =>
                        item.RelativeAssetPath is not null &&
                        item.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true))
                {
                    throw new InvalidOperationException(
                        "The privacy-isolated Codex review accepts reviewed text and images only. Remove binary attachments, choose Claude, or save the bundle for a manual handoff.");
                }

                var outbound = new AgentHandoffReview(
                    provider,
                    review,
                    included.Count(item => item.RelativeAssetPath is not null),
                    included.Where(item => item.RelativeAssetPath is not null).Sum(item => item.SizeBytes),
                    included.Any(item => item.HasPixels),
                    _paths.TempExportsDirectory);
                if (!_confirmation.Confirm(outbound))
                {
                    StatusText = "Nothing was sent. The reviewed packet remains local.";
                    return;
                }

                if (!string.Equals(_currentReview?.OutboundSha256, review.OutboundSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The visible draft changed during confirmation. Review the updated packet before sending.");
                }

                StatusText = "Materializing the approved packet in managed temporary storage…";
                bundle = await _exports.ExportAsync(
                    review,
                    included,
                    _paths.TempExportsDirectory,
                    cancellationToken).ConfigureAwait(true);

                if (!string.Equals(_currentReview?.OutboundSha256, review.OutboundSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The visible draft changed while the packet was materialized. Review it again before sending.");
                }

                StatusText = $"{provider.DisplayName} is reviewing the frozen packet…";
                string result = await _agents.AnalyzeAsync(new AgentAnalyzeRequest
                {
                    ProviderId = provider.Id,
                    WorkingDirectory = bundle.DirectoryPath,
                    ExactPrompt = review.OutboundMarkdown,
                    ImagePaths = bundle.ImagePaths,
                }, cancellationToken).ConfigureAwait(true);
                _resultReviewSha256 = review.OutboundSha256;
                _resultProviderName = provider.DisplayName;
                ResultText = result;
                OnPropertyChanged(nameof(IsResultStale));
                OnPropertyChanged(nameof(ResultIntegrityLabel));
                SelectedWorkspaceTabIndex = 2;
                StatusText = $"Read-only review received from {provider.DisplayName}. The result is ephemeral until you copy it.";
            }).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Agent handoff failed.");
                SetError(ex.Message);
            }
            finally
            {
                if (bundle is not null)
                {
                    DeleteOwnedBundle(bundle.DirectoryPath);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (enteredDraftGate)
            {
                _draftMutationGate.Release();
            }
        }
    }

    private bool CanCopyResult() => !IsBusy && HasResult;

    [RelayCommand(CanExecute = nameof(CanCopyResult))]
    private void CopyResult()
    {
        try
        {
            _clipboard.SetText(ResultText);
            StatusText = "Agent result copied.";
        }
        catch (Exception)
        {
            SetError("The agent result could not be copied.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyResult))]
    private async Task ReadResultAsync()
    {
        CommandResult result = await _commands.DispatchAsync(OctadockCommand.Create(
            CommandType.ReadAloud,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["text"] = ResultText,
            })).ConfigureAwait(true);
        StatusText = result.Message ?? "Reading the agent result aloud.";
    }

    private bool CanOpenExport() => HasExport;

    [RelayCommand(CanExecute = nameof(CanOpenExport))]
    private void OpenExport()
    {
        if (!Directory.Exists(LastExportedDirectory))
        {
            SetError("The exported packet folder no longer exists.");
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = LastExportedDirectory, UseShellExecute = true });
    }

    private bool CanCancelOperation() => IsBusy && _operationCancellation is not null;

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation() => _operationCancellation?.Cancel();

    private async Task AddPathCoreAsync(
        string path,
        CancellationToken cancellationToken,
        bool ownedTemporary = false,
        string? label = null,
        AgentPacketProvenance? provenance = null)
    {
        // Ownership is an explicit capability transferred by the pin lease or
        // created by this workspace. A filename that merely resembles one of
        // our temporary pins must never grant deletion authority.
        bool ownsPath = ownedTemporary;
        AgentWorkspaceEvidence? evidence = null;
        try
        {
            evidence = await _evidenceFactory.FromFileAsync(
                    path,
                    label: label,
                    provenance: provenance,
                    ownedTemporary: ownsPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(true);
            if (AddEvidence(evidence))
            {
                StatusText = $"Added '{evidence.Label}' with bounded content and a review hash.";
            }
        }
        catch
        {
            if (ownsPath && (evidence is null || !Evidence.Contains(evidence)))
            {
                DeleteOwnedTemporaryPath(path);
            }

            throw;
        }
    }

    private async Task AddClipboardCoreAsync(CancellationToken cancellationToken)
    {
        string? text = _clipboard.TryGetText();
        EncodedImage? image = _clipboard.TryGetImage();
        int requested = (string.IsNullOrWhiteSpace(text) ? 0 : 1) + (image is null ? 0 : 1);
        if (requested == 0)
        {
            throw new InvalidOperationException("The clipboard has no usable text or image.");
        }

        if (Evidence.Count > AgentPacketLimits.MaxSources - requested)
        {
            throw new InvalidOperationException(
                $"The clipboard needs {requested:N0} evidence slot(s), but this packet is already at its source limit.");
        }

        long existingBytes = Evidence.Where(item => item.RelativeAssetPath is not null).Sum(item => item.SizeBytes);
        long imageBytes = image?.Bytes.Length ?? 0;
        if (existingBytes > AgentEvidenceFactory.MaxPacketAttachmentBytes - imageBytes)
        {
            throw new InvalidOperationException(
                $"Packet attachments are limited to {AgentEvidenceFactory.MaxPacketAttachmentBytes / (1024 * 1024):N0} MB in total.");
        }

        AgentWorkspaceEvidence? stagedImage = null;
        try
        {
            if (image is not null)
            {
                stagedImage = await _evidenceFactory
                    .FromClipboardImageAsync(image, cancellationToken)
                    .ConfigureAwait(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            int added = 0;
            if (!string.IsNullOrWhiteSpace(text) && AddEvidence(_evidenceFactory.CreateText(
                    "Clipboard text",
                    text,
                    AgentPacketProvenanceKind.Clipboard)))
            {
                added++;
            }

            if (stagedImage is not null && AddEvidence(stagedImage))
            {
                added++;
                stagedImage = null;
            }

            StatusText = added == 1
                ? "Clipboard evidence added."
                : "Clipboard text and image added as separate evidence.";
        }
        finally
        {
            if (stagedImage is not null)
            {
                DeleteOwnedEvidence(stagedImage);
            }
        }
    }

    private async Task AddContextCoreAsync(
        ContextPackage package,
        CancellationToken cancellationToken,
        IReadOnlySet<Guid>? includedItemIds = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        int capacity = AgentPacketLimits.MaxSources - Evidence.Count;
        if (capacity <= 0)
        {
            throw new InvalidOperationException($"A packet accepts at most {AgentPacketLimits.MaxSources:N0} evidence items.");
        }

        var existingReferences = Evidence
            .Select(item => item.Provenance.Reference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.Ordinal);
        List<ContextItem> selectedItems = package.Items
            .Where(item => includedItemIds is null || includedItemIds.Contains(item.Id))
            .ToList();
        if (includedItemIds is not null)
        {
            HashSet<Guid> availableIds = selectedItems.Select(item => item.Id).ToHashSet();
            Guid[] missingIds = includedItemIds.Where(id => !availableIds.Contains(id)).ToArray();
            if (missingIds.Length > 0)
            {
                throw new InvalidOperationException(
                    $"{missingIds.Length:N0} selected Context item(s) no longer exist. Reopen Context and review the selection.");
            }
        }

        List<ContextItem> candidates = selectedItems
            .Where(item => !existingReferences.Contains($"context:{package.Id:N}/{item.Id:N}"))
            .ToList();
        int duplicateCount = selectedItems.Count - candidates.Count;
        int skippedForCapacity = Math.Max(0, candidates.Count - capacity);
        if (includedItemIds is not null && skippedForCapacity > 0)
        {
            throw new InvalidOperationException(
                $"The exact Context selection has {candidates.Count:N0} new item(s), but this packet has room for only {capacity:N0}. Remove evidence or select fewer items.");
        }

        var staged = new List<AgentWorkspaceEvidence>(Math.Min(capacity, candidates.Count));
        var committed = new List<AgentWorkspaceEvidence>(Math.Min(capacity, candidates.Count));
        try
        {
            foreach (ContextItem item in candidates.Take(capacity))
            {
                cancellationToken.ThrowIfCancellationRequested();
                staged.Add(await _evidenceFactory
                    .FromContextItemAsync(package, item, cancellationToken)
                    .ConfigureAwait(true));
            }

            EnsureBatchFits(staged);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (AgentWorkspaceEvidence evidence in staged)
            {
                if (AddEvidence(evidence))
                {
                    committed.Add(evidence);
                }
            }

            int added = committed.Count;
            staged.Clear();

            if (package.Items.Count == 0)
            {
                StatusText = "That Context package is empty.";
                return;
            }

            var details = new List<string> { $"{added:N0} added" };
            if (duplicateCount > 0)
            {
                details.Add($"{duplicateCount:N0} already present");
            }

            if (skippedForCapacity > 0)
            {
                details.Add($"{skippedForCapacity:N0} skipped at the packet limit");
            }

            StatusText = $"Context '{package.Name}': {string.Join(", ", details)}.";
        }
        catch
        {
            // Exact Context launches are transactional from the user's point of
            // view. If packet validation rejects a later item, remove anything
            // this batch already attached instead of leaving a partial handoff.
            foreach (AgentWorkspaceEvidence evidence in committed)
            {
                Evidence.Remove(evidence);
                DeleteOwnedEvidence(evidence);
                staged.Remove(evidence);
            }

            throw;
        }
        finally
        {
            foreach (AgentWorkspaceEvidence item in staged)
            {
                DeleteOwnedEvidence(item);
            }
        }
    }

    private void EnsureBatchFits(IReadOnlyCollection<AgentWorkspaceEvidence> staged)
    {
        if (Evidence.Count > AgentPacketLimits.MaxSources - staged.Count)
        {
            throw new InvalidOperationException($"A packet accepts at most {AgentPacketLimits.MaxSources:N0} evidence items.");
        }

        long existingBytes = Evidence.Where(item => item.RelativeAssetPath is not null).Sum(item => item.SizeBytes);
        long stagedBytes = staged.Where(item => item.RelativeAssetPath is not null).Sum(item => item.SizeBytes);
        if (existingBytes > AgentEvidenceFactory.MaxPacketAttachmentBytes - stagedBytes)
        {
            throw new InvalidOperationException(
                $"Packet attachments are limited to {AgentEvidenceFactory.MaxPacketAttachmentBytes / (1024 * 1024):N0} MB in total.");
        }
    }

    private async Task<bool> AddEvidenceAsync(Task<AgentWorkspaceEvidence> evidenceTask)
        => AddEvidence(await evidenceTask.ConfigureAwait(true));

    private bool AddEvidence(AgentWorkspaceEvidence evidence)
    {
        if (_disposed)
        {
            DeleteOwnedEvidence(evidence);
            throw new OperationCanceledException("The handoff review was closed.");
        }

        string? stableReference = evidence.Provenance.Reference;
        if (!string.IsNullOrWhiteSpace(stableReference) &&
            (stableReference.StartsWith("capture:", StringComparison.Ordinal) ||
             stableReference.StartsWith("context:", StringComparison.Ordinal)) &&
            Evidence.FirstOrDefault(item => string.Equals(
                item.Provenance.Reference,
                stableReference,
                StringComparison.Ordinal)) is { } existing)
        {
            DeleteOwnedEvidence(evidence);
            SelectedEvidence = existing;
            StatusText = "That evidence is already in the current draft.";
            return false;
        }

        if (Evidence.Count >= AgentPacketLimits.MaxSources)
        {
            DeleteOwnedEvidence(evidence);
            throw new InvalidOperationException($"A packet accepts at most {AgentPacketLimits.MaxSources:N0} evidence items.");
        }

        long currentAttachmentBytes = Evidence
            .Where(item => item.RelativeAssetPath is not null)
            .Sum(item => item.SizeBytes);
        long addedAttachmentBytes = evidence.RelativeAssetPath is null ? 0 : evidence.SizeBytes;
        if (currentAttachmentBytes > AgentEvidenceFactory.MaxPacketAttachmentBytes - addedAttachmentBytes)
        {
            DeleteOwnedEvidence(evidence);
            throw new InvalidOperationException(
                $"Packet attachments are limited to {AgentEvidenceFactory.MaxPacketAttachmentBytes / (1024 * 1024):N0} MB in total.");
        }

        ClearError();
        evidence.PropertyChanged += OnEvidenceChanged;
        Evidence.Add(evidence);
        if (HasError)
        {
            string validationError = ErrorText;
            Evidence.Remove(evidence);
            DeleteOwnedEvidence(evidence);
            throw new InvalidOperationException(validationError);
        }

        SelectedEvidence = evidence;
        return true;
    }

    private void OnEvidenceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AgentWorkspaceEvidence item in e.OldItems)
            {
                item.PropertyChanged -= OnEvidenceChanged;
            }
        }

        RefreshReview();
    }

    private void OnEvidenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_syncingVerificationSelection &&
            e.PropertyName == nameof(AgentWorkspaceEvidence.IsIncluded) &&
            sender is AgentWorkspaceEvidence changed &&
            changed.Provenance.Kind == AgentPacketProvenanceKind.VisualVerification &&
            !string.IsNullOrWhiteSpace(changed.Provenance.Reference))
        {
            try
            {
                _syncingVerificationSelection = true;
                foreach (AgentWorkspaceEvidence peer in Evidence.Where(item =>
                             item.Provenance.Kind == AgentPacketProvenanceKind.VisualVerification &&
                             string.Equals(
                                 item.Provenance.Reference,
                                 changed.Provenance.Reference,
                                 StringComparison.Ordinal)))
                {
                    peer.IsIncluded = changed.IsIncluded;
                }
            }
            finally
            {
                _syncingVerificationSelection = false;
            }
        }

        if (e.PropertyName is nameof(AgentWorkspaceEvidence.IsIncluded) or nameof(AgentWorkspaceEvidence.TextContent))
        {
            RefreshReview();
        }
    }

    private void RefreshReview()
    {
        if (_disposed)
        {
            return;
        }

        string? previousReviewHash = _currentReview?.OutboundSha256;
        ClearError();
        try
        {
            List<AgentWorkspaceEvidence> included = Evidence.Where(item => item.IsIncluded).ToList();
            if (string.IsNullOrWhiteSpace(Goal) || included.Count == 0)
            {
                _currentReview = null;
                PacketPreview = string.Empty;
                DetectedSecretCount = 0;
                LastExportedDirectory = string.Empty;
                _lastExportReviewSha256 = null;
                StatusText = string.IsNullOrWhiteSpace(Goal)
                    ? "Describe the outcome the agent should achieve."
                    : "Add at least one evidence item. Nothing is sent automatically.";
                NotifyReviewChanged();
                return;
            }

            List<AgentPacketAcceptanceCriterion> criteria = ParseCriteria();
            _currentReview = _packetBuilder.Build(new AgentPacketBuildRequest
            {
                Metadata = new AgentPacketMetadata
                {
                    Id = _packetId,
                    Title = string.IsNullOrWhiteSpace(TaskTitle) ? "Agent task" : TaskTitle,
                    Goal = Goal,
                    CreatedAt = _createdAt,
                    ProjectName = NullIfBlank(ProjectName),
                    TargetApplication = NullIfBlank(TargetApplication),
                    Environment = NullIfBlank(Environment),
                },
                Sources = included.Select(item => item.ToPacketSource()).ToList(),
                AcceptanceCriteria = criteria,
                RedactSecrets = RedactSecrets,
            });
            PacketPreview = _currentReview.OutboundMarkdown;
            DetectedSecretCount = _currentReview.DetectedSecretCount;
            if (!string.IsNullOrWhiteSpace(_lastExportReviewSha256) &&
                !string.Equals(
                    _lastExportReviewSha256,
                    _currentReview.OutboundSha256,
                    StringComparison.Ordinal))
            {
                LastExportedDirectory = string.Empty;
                _lastExportReviewSha256 = null;
            }
            if (HasResult && !string.Equals(
                    previousReviewHash,
                    _currentReview.OutboundSha256,
                    StringComparison.Ordinal))
            {
                SelectedWorkspaceTabIndex = 0;
            }
            StatusText = "Review the exact task packet, privacy boundary, evidence, and completion contract.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _currentReview = null;
            PacketPreview = string.Empty;
            DetectedSecretCount = 0;
            LastExportedDirectory = string.Empty;
            _lastExportReviewSha256 = null;
            SetError(ex.Message);
        }

        NotifyReviewChanged();
    }

    private List<AgentPacketAcceptanceCriterion> ParseCriteria()
    {
        List<string> lines = AcceptanceCriteriaText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Add at least one concrete acceptance criterion.");
        }

        return lines.Select((line, index) => new AgentPacketAcceptanceCriterion
        {
            Id = $"ac-{index + 1}",
            Description = line,
            IsRequired = true,
        }).ToList();
    }

    private void NotifyReviewChanged()
    {
        OnPropertyChanged(nameof(HasReview));
        OnPropertyChanged(nameof(HasEvidence));
        OnPropertyChanged(nameof(NeedsEvidence));
        OnPropertyChanged(nameof(NeedsOutcome));
        OnPropertyChanged(nameof(HasDetectedSecrets));
        OnPropertyChanged(nameof(IncludesPixelEvidence));
        OnPropertyChanged(nameof(IncludesUnscannedAttachments));
        OnPropertyChanged(nameof(PrivacyLabel));
        OnPropertyChanged(nameof(EvidenceSummary));
        OnPropertyChanged(nameof(EvidenceStepSummary));
        OnPropertyChanged(nameof(ReviewHash));
        OnPropertyChanged(nameof(IsResultStale));
        OnPropertyChanged(nameof(ResultIntegrityLabel));
        OnPropertyChanged(nameof(GuideTitle));
        OnPropertyChanged(nameof(GuideDescription));
        OnPropertyChanged(nameof(GuideStepLabel));
        OnPropertyChanged(nameof(AnalyzeButtonText));
        ChooseWorkflowCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        ExportPacketCommand.NotifyCanExecuteChanged();
        CopyPacketCommand.NotifyCanExecuteChanged();
        RunOcrCommand.NotifyCanExecuteChanged();
    }

    private async Task RunBusyAsync(
        string status,
        Func<CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        _operationCancellation = cancellation;
        IsBusy = true;
        StatusText = status;
        try
        {
            await action(cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Operation cancelled. Source files and the current draft were not changed.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent Workspace operation failed.");
            SetError(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            IsBusy = false;
        }
    }

    private void ResetDraftForLaunch()
    {
        foreach (AgentWorkspaceEvidence item in Evidence.ToList())
        {
            item.PropertyChanged -= OnEvidenceChanged;
            DeleteOwnedEvidence(item);
        }

        SelectedEvidence = null;
        Evidence.Clear();
        _packetId = $"task-{Guid.NewGuid():N}";
        _createdAt = DateTimeOffset.UtcNow;
        SelectedWorkflow = null;
        _workflowBaseGoal = string.Empty;
        AdditionalIntent = string.Empty;
        Goal = string.Empty;
        TaskTitle = "Untitled agent task";
        AcceptanceCriteriaText = string.Empty;
        ProjectName = string.Empty;
        TargetApplication = string.Empty;
        Environment = string.Empty;
        RedactSecrets = true;
        VerificationSummary = string.Empty;
        LastExportedDirectory = string.Empty;
        _lastExportReviewSha256 = null;
        _resultReviewSha256 = null;
        _resultProviderName = null;
        ResultText = string.Empty;
        SelectedWorkspaceTabIndex = 0;
        ReviewHeading = "Review handoff";
        ReviewSourceLabel = "Octadock · New reviewed handoff";
        ReviewWindowTitle = "Octadock · Review handoff";
        _currentReview = null;
        PacketPreview = string.Empty;
        DetectedSecretCount = 0;
        ClearError();
        NotifyReviewChanged();
    }

    private void ApplyReviewSource(OctadockCommand command)
    {
        string displaySource = AgentReviewLaunch.DisplaySource(
            command.Get(AgentReviewLaunch.ReviewSourceParameter));
        string label = AgentReviewLaunch.NormalizeLabel(
            command.Get(AgentReviewLaunch.ReviewLabelParameter));

        ReviewHeading = displaySource == "Octadock"
            ? "Review handoff"
            : $"Review from {displaySource}";
        ReviewSourceLabel = string.IsNullOrWhiteSpace(label)
            ? $"{displaySource} · Explicit reviewed handoff"
            : $"{displaySource} · {label}";
        ReviewWindowTitle = $"Octadock · {ReviewHeading}";
    }

    private async Task ResetToHomeAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            StatusText = "Finish or cancel the current AI workflow before starting another one.";
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        bool enteredLaunch = false;
        bool enteredDraft = false;
        try
        {
            await _launchGate.WaitAsync(linked.Token).ConfigureAwait(true);
            enteredLaunch = true;
            await _draftMutationGate.WaitAsync(linked.Token).ConfigureAwait(true);
            enteredDraft = true;
            ResetDraftForLaunch();
            StatusText = "Choose an outcome, or start from Capture, Pin, Context, History, Clipboard, or Voice to preload evidence.";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (enteredDraft)
            {
                _draftMutationGate.Release();
            }

            if (enteredLaunch)
            {
                _launchGate.Release();
            }
        }
    }

    private void ApplyWorkflow(AgentWorkflowDefinition workflow, bool replaceIntent)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        SelectedWorkflow = workflow;
        if (replaceIntent || string.IsNullOrWhiteSpace(Goal))
        {
            _workflowBaseGoal = workflow.Goal;
            AdditionalIntent = string.Empty;
            Goal = _workflowBaseGoal;
            AcceptanceCriteriaText = workflow.CriteriaText;
            TaskTitle = workflow.Title;
        }
    }

    private string BuildLaunchStatus(AgentWorkflowDefinition? workflow)
    {
        if (Evidence.Count == 0)
        {
            return workflow is null
                ? "Step 1: add a screenshot, clipboard item, Context package, or file."
                : $"Step 1: add the evidence for '{workflow.Title}'.";
        }

        if (workflow is null)
        {
            return "Step 2: evidence is attached. Choose what result you want from it.";
        }

        if (workflow.IsVisualVerification && Evidence.Count(item => item.HasPixels) < 2)
        {
            return "Baseline evidence is ready. Add the result image with Before / after to verify it.";
        }

        return $"Step 3: '{workflow.Title}' is ready. Add only missing context, then run.";
    }

    private static string LegacyGoal(string? action) => action?.Trim().ToLowerInvariant() switch
    {
        "summarize" or "summary" => "Use the supplied evidence to produce the specific deliverable I describe here. Preserve material facts and uncertainty.",
        "explain" => "Investigate the supplied evidence and explain the underlying cause, implications, and concrete next step. Do not stop at a generic description.",
        "clean" or "rewrite" or "clean-rewrite" or "cleanrewrite" => "Transform the supplied artifact into a production-ready deliverable while preserving its factual meaning and constraints.",
        "actions" or "tasks" or "action-items" or "extract-action-items" => "Turn the supplied evidence into an executable plan with concrete outcomes, dependencies, owners when known, and verification steps.",
        _ => string.Empty,
    };

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatBytes(long bytes)
    {
        double value = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB"];
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return index == 0 ? $"{value:0} {units[index]}" : $"{value:0.#} {units[index]}";
    }

    private void DeleteOwnedBundle(string directory)
    {
        try
        {
            string tempRoot = AgentLocalPathGuard.ValidateExistingDirectory(
                _paths.TempExportsDirectory,
                "Agent Workspace temporary storage");
            string full = AgentLocalPathGuard.ValidateExistingDirectory(directory, "Owned Agent Packet");
            string relative = Path.GetRelativePath(tempRoot, full);
            if (!Path.IsPathRooted(relative) &&
                relative.StartsWith("octadock-agent-", StringComparison.Ordinal) &&
                !relative.Contains(Path.DirectorySeparatorChar) &&
                Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
        }
    }

    private void DeleteOwnedEvidence(AgentWorkspaceEvidence evidence)
    {
        if (!evidence.IsOwnedTemporary || string.IsNullOrWhiteSpace(evidence.LocalPath))
        {
            return;
        }

        DeleteOwnedTemporaryPath(evidence.LocalPath);
    }

    private void DeleteOwnedTemporaryPath(string path)
    {
        try
        {
            string tempRoot = AgentLocalPathGuard.ValidateExistingDirectory(
                _paths.TempExportsDirectory,
                "Agent Workspace temporary storage");
            string full = AgentLocalPathGuard.ValidateExistingFile(path, "Owned temporary evidence");
            string relative = Path.GetRelativePath(tempRoot, full);
            if (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
        }
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private void ClearError()
    {
        HasError = false;
        ErrorText = string.Empty;
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorText = message;
        StatusText = "The packet needs attention before it can be handed off.";
        NotifyReviewChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        lock (_launchSync)
        {
            _launchCancellation?.Cancel();
        }
        _operationCancellation?.Cancel();
        foreach (AgentWorkspaceEvidence item in Evidence)
        {
            item.PropertyChanged -= OnEvidenceChanged;
            DeleteOwnedEvidence(item);
        }

        Evidence.CollectionChanged -= OnEvidenceCollectionChanged;
    }
}
