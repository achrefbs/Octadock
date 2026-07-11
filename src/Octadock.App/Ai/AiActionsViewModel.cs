using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Commands;

namespace Octadock.App.Ai;

public sealed record AiActionOption(AiTextActionKind Kind, string Label, string Description);

public sealed record AiProviderOption(AiCliProviderDescriptor Descriptor)
{
    public string Label => Descriptor.IsAvailable
        ? $"{Descriptor.DisplayName} CLI"
        : $"{Descriptor.DisplayName} CLI — not found";
}

/// <summary>
/// One reviewed text-artifact workflow. Input and result live only in this window;
/// every edit rebuilds the exact outbound preview and every execution asks for a
/// fresh destination-named confirmation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class AiActionsViewModel : ObservableObject, IDisposable
{
    private readonly IAiTextActionService _actions;
    private readonly IAiSendConfirmation _confirmation;
    private readonly IClipboardService _clipboard;
    private readonly IAiTextFilePicker _filePicker;
    private readonly IAiTextFileLoader _fileLoader;
    private readonly ICommandDispatcher _dispatcher;

    private AiOutboundReview? _currentReview;
    private CancellationTokenSource? _sendCancellation;

    [ObservableProperty]
    private AiActionOption _selectedAction;

    [ObservableProperty]
    private AiProviderOption _selectedProvider;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _sourceName = "Pasted or typed text";

    [ObservableProperty]
    private bool _redactSecrets = true;

    [ObservableProperty]
    private string _outboundPreview = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Add text to build an outbound preview.";

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _detectedSecretCount;

    public AiActionsViewModel(
        IAiTextActionService actions,
        IAiSendConfirmation confirmation,
        IClipboardService clipboard,
        IAiTextFilePicker filePicker,
        IAiTextFileLoader fileLoader,
        ICommandDispatcher dispatcher)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _fileLoader = fileLoader ?? throw new ArgumentNullException(nameof(fileLoader));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        Actions =
        [
            new(AiTextActionKind.Explain, "Explain", "Plain-language meaning without invented facts"),
            new(AiTextActionKind.Summarize, "Summarize", "Main point and material details, concisely"),
            new(AiTextActionKind.CleanRewrite, "Clean rewrite", "Clearer grammar and flow, same meaning"),
            new(AiTextActionKind.ExtractActionItems, "Extract action items", "Concrete tasks, owners, and dates when stated"),
        ];
        Providers = _actions.Providers.Select(provider => new AiProviderOption(provider)).ToList();
        if (Providers.Count == 0)
        {
            throw new InvalidOperationException("No supported AI CLI providers are configured.");
        }

        _selectedAction = Actions[0];
        _selectedProvider = Providers.FirstOrDefault(provider => provider.Descriptor.IsAvailable) ?? Providers[0];
        RefreshReview(clearResult: false);
    }

    public IReadOnlyList<AiActionOption> Actions { get; }

    public IReadOnlyList<AiProviderOption> Providers { get; }

    public bool IsNotBusy => !IsBusy;

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    public bool HasDetectedSecrets => DetectedSecretCount > 0;

    public string SendButtonText => $"Send to {SelectedProvider.Descriptor.DisplayName}";

    public string ProviderDisclosure =>
        $"Provider: {SelectedProvider.Descriptor.DisplayName} CLI  ·  Destination: {SelectedProvider.Descriptor.DestinationDisclosure}";

    public string ReviewEstimate => _currentReview is null
        ? "0 characters"
        : $"{_currentReview.OutboundCharacterCount:N0} characters  ·  about {Math.Max(1, (_currentReview.OutboundCharacterCount + 3) / 4):N0} tokens";

    public string SecretSummary => DetectedSecretCount switch
    {
        0 => "No common secrets detected",
        _ when RedactSecrets => $"{DetectedSecretCount:N0} detected  ·  redacted in preview",
        _ => $"{DetectedSecretCount:N0} detected  ·  will be sent unchanged",
    };

    partial void OnSelectedActionChanged(AiActionOption value) => RefreshReview();

    partial void OnSelectedProviderChanged(AiProviderOption value)
    {
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(ProviderDisclosure));
        RefreshReview();
    }

    partial void OnInputTextChanged(string value) => RefreshReview();

    partial void OnSourceNameChanged(string value) => RefreshReview();

    partial void OnRedactSecretsChanged(bool value) => RefreshReview();

    partial void OnResultTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasResult));
        CopyResultCommand.NotifyCanExecuteChanged();
        ReadAloudResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        SendCommand.NotifyCanExecuteChanged();
        CancelSendCommand.NotifyCanExecuteChanged();
        CopyResultCommand.NotifyCanExecuteChanged();
        ReadAloudResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnDetectedSecretCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDetectedSecrets));
        OnPropertyChanged(nameof(SecretSummary));
    }

    private void RefreshReview(bool clearResult = true)
    {
        if (clearResult && !IsBusy)
        {
            ResultText = string.Empty;
        }

        ClearError();
        if (string.IsNullOrWhiteSpace(InputText))
        {
            _currentReview = null;
            OutboundPreview = string.Empty;
            DetectedSecretCount = 0;
            StatusText = "Add text to build an outbound preview.";
            NotifyReviewChanged();
            return;
        }

        try
        {
            _currentReview = _actions.Review(new AiTextActionRequest
            {
                Action = SelectedAction.Kind,
                Text = InputText,
                SourceName = SourceName,
                ProviderId = SelectedProvider.Descriptor.Id,
                RedactSecrets = RedactSecrets,
            });
            OutboundPreview = _currentReview.OutboundText;
            DetectedSecretCount = _currentReview.DetectedSecretCount;
            StatusText = SelectedProvider.Descriptor.IsAvailable
                ? "Review the exact outbound preview, then choose the destination-named send button."
                : SelectedProvider.Descriptor.UnavailableReason ?? "The selected CLI is unavailable.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _currentReview = null;
            OutboundPreview = string.Empty;
            DetectedSecretCount = 0;
            SetError(ex.Message);
        }

        NotifyReviewChanged();
    }

    private void NotifyReviewChanged()
    {
        OnPropertyChanged(nameof(ReviewEstimate));
        OnPropertyChanged(nameof(SecretSummary));
        SendCommand.NotifyCanExecuteChanged();
    }

    private bool CanSend()
        => !IsBusy && _currentReview is not null && SelectedProvider.Descriptor.IsAvailable;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        AiOutboundReview? review = _currentReview;
        if (review is null || !_confirmation.Confirm(review))
        {
            StatusText = "Nothing was sent.";
            return;
        }

        ClearError();
        var sendCancellation = new CancellationTokenSource();
        _sendCancellation = sendCancellation;
        IsBusy = true;
        StatusText = $"Sending the reviewed preview to {review.ProviderDisplayName}…";
        try
        {
            AiTextActionResult result = await _actions.ExecuteReviewedAsync(review, sendCancellation.Token);
            ResultText = result.Text;
            StatusText = $"Result received from {review.ProviderDisplayName}. It is not saved by Octadock.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Send cancelled. No result was saved.";
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_sendCancellation, sendCancellation))
            {
                _sendCancellation = null;
            }

            sendCancellation.Dispose();
            IsBusy = false;
        }
    }

    private bool CanCancelSend() => IsBusy && _sendCancellation is not null;

    [RelayCommand(CanExecute = nameof(CanCancelSend))]
    private void CancelSend() => _sendCancellation?.Cancel();

    [RelayCommand]
    private void PasteInput()
    {
        try
        {
            string? text = _clipboard.TryGetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetError("The clipboard does not contain text.");
                return;
            }

            SourceName = "Clipboard";
            InputText = text;
        }
        catch (Exception)
        {
            SetError("Clipboard text could not be read. Try copying it again.");
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        string? path = _filePicker.PickFile();
        if (path is null)
        {
            return;
        }

        await LoadFileAsync(path, CancellationToken.None);
    }

    [RelayCommand]
    private void Clear()
    {
        SourceName = "Pasted or typed text";
        InputText = string.Empty;
        ResultText = string.Empty;
        ClearError();
    }

    private bool CanUseResult() => !IsBusy && HasResult;

    [RelayCommand(CanExecute = nameof(CanUseResult))]
    private void CopyResult()
    {
        try
        {
            _clipboard.SetText(ResultText);
            StatusText = "Result copied to the clipboard.";
        }
        catch (Exception)
        {
            SetError("The result could not be copied. Try again.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseResult))]
    private async Task ReadAloudResultAsync()
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = ResultText,
            ["source"] = $"{SelectedAction.Label} result",
        };
        CommandResult result = await _dispatcher.DispatchAsync(
            OctadockCommand.Create(CommandType.ReadAloud, parameters));
        if (!result.Success)
        {
            SetError(result.Message ?? "The result could not be read aloud.");
        }
        else
        {
            StatusText = "Reading the result aloud.";
        }
    }

    public async Task ApplyLaunchCommandAsync(
        OctadockCommand? command,
        CancellationToken cancellationToken = default)
    {
        if (command is null || IsBusy)
        {
            return;
        }

        string? requestedAction = command.Get("action");
        AiTextActionKind action = AiTextActionKind.Explain;
        if (!string.IsNullOrWhiteSpace(requestedAction) &&
            !TryParseAction(requestedAction, out action))
        {
            SetError("Action must be explain, summarize, clean-rewrite, or action-items.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(requestedAction))
        {
            SelectedAction = Actions.Single(option => option.Kind == action);
        }

        string? provider = command.Get("provider");
        if (!string.IsNullOrWhiteSpace(provider))
        {
            AiProviderOption? match = Providers.FirstOrDefault(option =>
                string.Equals(option.Descriptor.Id, provider, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                SetError("Provider must be 'codex' or 'claude'.");
                return;
            }

            SelectedProvider = match;
        }

        string? literal = command.Get("text");
        if (!string.IsNullOrWhiteSpace(literal))
        {
            SourceName = command.Get("source") ?? "Command text";
            InputText = literal;
            return;
        }

        if (!string.IsNullOrWhiteSpace(command.FilePath))
        {
            await LoadFileAsync(command.FilePath, cancellationToken);
            return;
        }

        if (command.GetBool("clipboard"))
        {
            PasteInput();
        }
    }

    private async Task LoadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            AiTextFileInput input = await _fileLoader.LoadAsync(path, cancellationToken);
            SourceName = input.SourceName;
            InputText = input.Text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError(ex.Message);
        }
    }

    private static bool TryParseAction(string? value, out AiTextActionKind action)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "explain":
                action = AiTextActionKind.Explain;
                return true;
            case "summarize":
            case "summary":
                action = AiTextActionKind.Summarize;
                return true;
            case "clean":
            case "rewrite":
            case "clean-rewrite":
                action = AiTextActionKind.CleanRewrite;
                return true;
            case "actions":
            case "action-items":
            case "extract-action-items":
            case "tasks":
                action = AiTextActionKind.ExtractActionItems;
                return true;
            default:
                action = AiTextActionKind.Explain;
                return false;
        }
    }

    private void SetError(string message)
    {
        ErrorText = message;
        HasError = true;
        StatusText = message;
    }

    private void ClearError()
    {
        ErrorText = string.Empty;
        HasError = false;
    }

    public void Dispose()
    {
        _sendCancellation?.Cancel();
    }
}
