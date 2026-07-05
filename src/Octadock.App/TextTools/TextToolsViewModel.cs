using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Octadock.Core.Abstractions;
using Octadock.Core.TextTools;

namespace Octadock.App.TextTools;

/// <summary>
/// The text-transform toolbox view model: pick a transform, paste/type input,
/// and the output updates live. Everything runs locally and instantly.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class TextToolsViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;

    [ObservableProperty]
    private TextTransformDescriptor _selectedTransform;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _outputText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>Creates the view model.</summary>
    public TextToolsViewModel(IClipboardService clipboard, INotificationService notifications)
    {
        _clipboard = clipboard;
        _notifications = notifications;
        Transforms = TextTransforms.Catalog;
        _selectedTransform = Transforms[0];
    }

    /// <summary>The transform catalog, grouped by category in the view.</summary>
    public IReadOnlyList<TextTransformDescriptor> Transforms { get; }

    partial void OnSelectedTransformChanged(TextTransformDescriptor value) => Apply();

    partial void OnInputTextChanged(string value) => Apply();

    private void Apply()
    {
        if (string.IsNullOrEmpty(InputText))
        {
            OutputText = string.Empty;
            ErrorText = string.Empty;
            HasError = false;
            return;
        }

        TextTransformResult result = TextTransforms.Apply(SelectedTransform.Kind, InputText);
        if (result.Success)
        {
            OutputText = result.Output;
            ErrorText = string.Empty;
            HasError = false;
        }
        else
        {
            OutputText = string.Empty;
            ErrorText = result.Error;
            HasError = true;
        }
    }

    /// <summary>Copies the output to the clipboard.</summary>
    [RelayCommand]
    public void CopyOutput()
    {
        if (string.IsNullOrEmpty(OutputText))
        {
            return;
        }

        try
        {
            _clipboard.SetText(OutputText);
            _notifications.Notify("Copied", "The result is on your clipboard.", NotificationKind.Success);
        }
        catch (Exception)
        {
            _notifications.Notify("Copy failed", "The result could not be copied.", NotificationKind.Error);
        }
    }

    /// <summary>Pulls the current clipboard text into the input box.</summary>
    [RelayCommand]
    public void PasteInput()
    {
        string? text = _clipboard.TryGetText();
        if (!string.IsNullOrEmpty(text))
        {
            InputText = text;
        }
    }

    /// <summary>Feeds the output back into the input for chained transforms.</summary>
    [RelayCommand]
    public void UseOutputAsInput()
    {
        if (!string.IsNullOrEmpty(OutputText))
        {
            InputText = OutputText;
        }
    }

    /// <summary>Clears both panes.</summary>
    [RelayCommand]
    public void Clear()
    {
        InputText = string.Empty;
    }
}
