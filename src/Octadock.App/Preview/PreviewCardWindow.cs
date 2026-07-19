using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.CaptureUx;
using Octadock.App.Context;
using Octadock.App.Theming;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.Preview;

/// <summary>
/// The Windows glass preview card: a dark glass, focus-taking tool window that
/// renders a <see cref="FilePreviewResult"/> (a sortable/filterable CSV table with
/// a stats footer, or a monospace plain-text body). It is built entirely in code
/// (no XAML) following the <c>DockPill</c> approach. Unlike the passive overlays
/// it derives from, it takes keyboard focus: <c>Esc</c> closes it and clicking
/// away (via <see cref="Window.Deactivated"/>) dismisses it. Opening plays a
/// spotlight animation (scale 0.96 to 1 with an opacity fade).
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class PreviewCardWindow : ToolWindowBase
{
    // Image pins define the product's visual language: transparent graphite
    // glass, neutral chrome, and a restrained teal accent only where state needs it.
    private static PreviewCardPalette CurrentPalette => CreatePreviewPalette(SystemParameters.HighContrast);
    private static Brush CardBackground => CurrentPalette.CardBackground;
    private static Brush ChromeBackground => CurrentPalette.ChromeBackground;
    private static Brush PanelBackground => CurrentPalette.PanelBackground;
    private static Brush GlassBorder => CurrentPalette.GlassBorder;
    private static Brush TextBrush => CurrentPalette.Text;
    private static Brush MutedBrush => CurrentPalette.MutedText;
    private static Brush AccentBrush => CurrentPalette.Accent;
    private static Brush WarmAccentBrush => CurrentPalette.SecondaryAccent;
    private static Brush FieldBackground => CurrentPalette.FieldBackground;
    private static Brush HeaderRule => CurrentPalette.Rule;
    private static Brush MenuBackground => CurrentPalette.MenuBackground;
    private static Brush MenuHover => CurrentPalette.Hover;
    private static Brush DangerHover => CurrentPalette.DangerHover;
    private static Brush ActiveActionBackground => SystemParameters.HighContrast
        ? SystemColors.ControlBrush
        : OctadockDesignTokens.Brushes.ActiveAction;
    private static Brush ActionHoverBackground => SystemParameters.HighContrast
        ? SystemColors.ControlBrush
        : OctadockDesignTokens.Brushes.ActionHover;

    private const string CardBackgroundResource = "Preview.CardBackground";
    private const string ChromeBackgroundResource = "Preview.ChromeBackground";
    private const string PanelBackgroundResource = "Preview.PanelBackground";
    private const string GlassBorderResource = "Preview.GlassBorder";
    private const string TextResource = "Preview.Text";
    private const string MutedTextResource = "Preview.MutedText";
    private const string AccentResource = "Preview.Accent";
    private const string SecondaryAccentResource = "Preview.SecondaryAccent";
    private const string FieldBackgroundResource = "Preview.FieldBackground";
    private const string RuleResource = "Preview.Rule";
    private const string MenuBackgroundResource = "Preview.MenuBackground";
    private const string MenuHoverResource = "Preview.MenuHover";
    private const string DangerHoverResource = "Preview.DangerHover";
    private const string ActiveActionResource = "Preview.ActiveAction";
    private const string ActionHoverResource = "Preview.ActionHover";
    private const string RowHoverResource = "Preview.RowHover";
    private const string RowSelectedResource = "Preview.RowSelected";
    private const string ScrollThumbResource = "Preview.ScrollThumb";
    private const string ScrollThumbHoverResource = "Preview.ScrollThumbHover";
    private const string ColumnHeaderTextResource = "Preview.ColumnHeaderText";
    private const string ColumnBorderResource = "Preview.ColumnBorder";
    private const string ColumnHoverResource = "Preview.ColumnHover";
    private const string ColumnPressedResource = "Preview.ColumnPressed";
    private const string HighlightTextResource = "Preview.HighlightText";
    private const string MenuHighlightTextResource = "Preview.MenuHighlightText";

    private const string CopyGlyph = "\uE8C8";
    private const string PathGlyph = "\uE71B";
    private const string FolderGlyph = "\uE8B7";
    private const string LaunchGlyph = "\uE8A7";
    private const string SaveGlyph = "\uE105";
    private const string AddGlyph = "\uE710";
    private const string FitGlyph = "\uE9A6";
    private const string InfoGlyph = "\uE946";
    private const string CloseGlyph = "\uE711";

    private const double DefaultPreviewMinWidth = 680;
    private const double DefaultPreviewMinHeight = 360;

    private static readonly Duration OpenDuration = new(TimeSpan.FromMilliseconds(200));

    private readonly Border _cardRoot;
    private readonly ScaleTransform _cardScale;
    private readonly TextBlock _titleText;
    private readonly TextBlock _subtitleText;
    private readonly TextBlock _fileKindText;
    private readonly TextBlock _providerKindText;
    private readonly TextBox _filterBox;
    private readonly StackPanel _filterHost;
    private readonly PreviewCardActions _actions;
    private readonly WrapPanel _actionHost;
    private readonly Button _copyContentButton;
    private readonly Button _copyFormattedButton;
    private readonly Button _addToContextButton;
    private readonly TextBlock _addToContextText;
    private readonly Button _addToShelfButton;
    private readonly Button _fitImageButton;
    private readonly Button _retryButton;
    private readonly Button _locateButton;
    private readonly Button _cancelButton;
    private readonly Button _toggleInspectorButton;
    private readonly ContentControl _bodyHost;
    private readonly Border _inspectorHost;
    private readonly List<ContextMenu> _contextMenus = new();

    private string? _currentFilePath;
    private string? _currentCopyContent;
    private string? _currentFormattedContent;
    private System.Windows.Controls.Image? _currentImage;
    private ScrollViewer? _currentImageScroll;
    private ICollectionView? _csvView;
    private CsvPreviewModel? _csvModel;
    private int _sortColumn = -1;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private bool _copyCsvFromView;
    private bool _fitImageToCard = true;
    private bool _inspectorVisible = true;
    private bool _suppressDeactivatedClose;
    private bool _modalInteractionActive;
    private readonly PreviewContextSelectionCloseGuard _contextSelectionCloseGuard = new();
    private bool _isClosing;
    private PixelRect? _pendingPhysicalBounds;
    private FlowDocumentScrollViewer? _currentMarkdownViewer;
    private string? _currentMarkdownSource;

    internal bool IsClosing => _isClosing;

    /// <summary>Creates the (reusable) preview card window.</summary>
    public PreviewCardWindow(PreviewCardActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _actions = actions;

        // The card owns keyboard focus, unlike the passive overlays in the base.
        ShowActivated = true;
        Focusable = true;
        Topmost = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = DefaultPreviewMinWidth;
        MinHeight = DefaultPreviewMinHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _cardScale = new ScaleTransform(0.96, 0.96);

        _titleText = new TextBlock
        {
            Foreground = TextBrush,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _titleText.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        _subtitleText = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        _subtitleText.SetResourceReference(TextBlock.ForegroundProperty, MutedTextResource);

        _fileKindText = new TextBlock
        {
            Text = "FILE",
            Foreground = TextBrush,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _fileKindText.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        _providerKindText = new TextBlock
        {
            Text = "FILE",
            Foreground = MutedBrush,
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 1, 0, 0),
        };
        _providerKindText.SetResourceReference(TextBlock.ForegroundProperty, MutedTextResource);

        _filterBox = new TextBox
        {
            Width = 220,
            Background = FieldBackground,
            Foreground = TextBrush,
            CaretBrush = TextBrush,
            SelectionBrush = AccentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Filter rows",
        };
        _filterBox.SetResourceReference(Control.BackgroundProperty, FieldBackgroundResource);
        _filterBox.SetResourceReference(Control.ForegroundProperty, TextResource);
        _filterBox.SetResourceReference(TextBox.CaretBrushProperty, TextResource);
        _filterBox.SetResourceReference(TextBox.SelectionBrushProperty, AccentResource);
        _filterBox.TextChanged += (_, _) => _csvView?.Refresh();
        AutomationProperties.SetName(_filterBox, "Filter preview rows");
        AutomationProperties.SetHelpText(_filterBox, "Filters only the rows in the visible CSV sample");

        _filterHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(8, 0, 0, 0),
            Children =
            {
                WithResource(
                    new TextBlock
                    {
                        Text = "Find",
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0),
                    },
                    (TextBlock.ForegroundProperty, MutedTextResource)),
                _filterBox,
            },
        };

        _actionHost = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
        };

        _copyContentButton = MakeActionButton(CopyGlyph, "Copy original content", CopyCurrentContent, "Copy original");
        _copyContentButton.Visibility = Visibility.Collapsed;
        _copyFormattedButton = MakeActionButton(CopyGlyph, "Copy formatted content", CopyFormattedContent, "Copy formatted JSON");
        _copyFormattedButton.Visibility = Visibility.Collapsed;
        _addToContextButton = MakeActionButton(AddGlyph, "Choose a Context…", AddCurrentFileToContext, "Choose a Context…");
        var contextGlyph = (TextBlock)_addToContextButton.Content;
        _addToContextButton.Content = null;
        _addToContextText = new TextBlock
        {
            Text = "Choose a Context…",
            Foreground = TextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
        };
        _addToContextText.SetResourceReference(TextBlock.ForegroundProperty, TextResource);
        _addToContextButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { contextGlyph, _addToContextText },
        };
        _addToContextButton.Width = double.NaN;
        _addToContextButton.Padding = new Thickness(8, 0, 8, 0);
        _addToContextButton.MinWidth = 32;
        _addToContextButton.Visibility = Visibility.Collapsed;
        _addToShelfButton = MakeActionButton(AddGlyph, "Add this image to the dock", () => InvokePathAction(_actions.AddToShelf), "Add to dock");
        _addToShelfButton.Visibility = Visibility.Collapsed;
        _fitImageButton = MakeActionButton(FitGlyph, "Show image at actual preview size", ToggleImageFitMode, "Actual image size");
        _fitImageButton.Visibility = Visibility.Collapsed;
        _retryButton = MakeActionButton("\uE72C", "Retry preview", () => InvokePathAction(_actions.Retry), "Retry preview");
        _retryButton.Visibility = Visibility.Collapsed;
        _locateButton = MakeActionButton(FolderGlyph, "Locate moved file", () => InvokeModalPathAction(_actions.Locate), "Locate file…");
        _locateButton.Visibility = Visibility.Collapsed;
        _cancelButton = MakeActionButton(CloseGlyph, "Cancel loading", _actions.Cancel, "Cancel loading");
        _cancelButton.Visibility = Visibility.Collapsed;
        _toggleInspectorButton = MakeActionButton(InfoGlyph, "Hide details", ToggleInspector, "Hide details");
        _actionHost.Children.Add(_copyContentButton);
        _actionHost.Children.Add(_copyFormattedButton);
        _actionHost.Children.Add(_addToContextButton);
        _actionHost.Children.Add(_addToShelfButton);
        _actionHost.Children.Add(_fitImageButton);
        _actionHost.Children.Add(_retryButton);
        _actionHost.Children.Add(_locateButton);
        _actionHost.Children.Add(_cancelButton);
        _actionHost.Children.Add(_toggleInspectorButton);
        _actionHost.Children.Add(MakeActionButton(PathGlyph, "Copy file path", () => InvokePathAction(_actions.CopyPath), "Copy path"));
        _actionHost.Children.Add(MakeActionButton(SaveGlyph, "Save a copy as", () => InvokeModalPathAction(_actions.SaveCopyAs), "Save as..."));
        _actionHost.Children.Add(MakeActionButton(FolderGlyph, "Show in File Explorer", () => InvokePathAction(_actions.RevealInExplorer), "Show in folder"));
        _actionHost.Children.Add(MakeActionButton(LaunchGlyph, "Open with default app", ConfirmOpenWithDefaultApp, "Open externally"));

        var actionBar = new Border
        {
            Background = ChromeBackground,
            BorderBrush = HeaderRule,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Rail,
            Child = _actionHost,
        };
        actionBar.SetResourceReference(Border.BackgroundProperty, ChromeBackgroundResource);
        actionBar.SetResourceReference(Border.BorderBrushProperty, RuleResource);

        Button closeButton = MakeActionButton(CloseGlyph, "Close (Esc)", RequestClose, "Close preview", DangerHoverResource);
        closeButton.Margin = new Thickness(8, 0, 0, 0);

        var titleStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _titleText, _subtitleText },
        };

        var dragSurface = new Grid
        {
            Cursor = Cursors.SizeAll,
            MinHeight = 44,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleStack },
        };
        dragSurface.MouseLeftButtonDown += OnDragSurfaceMouseLeftButtonDown;

        var header = new Grid { Margin = new Thickness(16, 10, 12, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement previewBadge = MakePreviewBadge();
        Grid.SetColumn(previewBadge, 0);
        Grid.SetColumn(dragSurface, 1);
        Grid.SetColumn(closeButton, 2);
        header.Children.Add(previewBadge);
        header.Children.Add(dragSurface);
        header.Children.Add(closeButton);

        var toolbarGrid = new Grid();
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionBar.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_filterHost, 0);
        Grid.SetColumn(actionBar, 1);
        toolbarGrid.Children.Add(_filterHost);
        toolbarGrid.Children.Add(actionBar);

        var toolbar = new Border
        {
            Background = ChromeBackground,
            BorderBrush = HeaderRule,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(12, 7, 12, 7),
            Child = toolbarGrid,
        };
        toolbar.SetResourceReference(Border.BackgroundProperty, ChromeBackgroundResource);
        toolbar.SetResourceReference(Border.BorderBrushProperty, RuleResource);

        _bodyHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetLiveSetting(_bodyHost, AutomationLiveSetting.Polite);

        _inspectorHost = new Border
        {
            Width = 236,
            Margin = new Thickness(0, 10, 12, 12),
            Background = ChromeBackground,
            BorderBrush = HeaderRule,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Rail,
            ClipToBounds = true,
        };
        _inspectorHost.SetResourceReference(Border.BackgroundProperty, ChromeBackgroundResource);
        _inspectorHost.SetResourceReference(Border.BorderBrushProperty, RuleResource);

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_bodyHost, 0);
        Grid.SetColumn(_inspectorHost, 1);
        contentGrid.Children.Add(_bodyHost);
        contentGrid.Children.Add(_inspectorHost);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(toolbar);
        layout.Children.Add(contentGrid);
        UpdateInspectorChrome();

        _cardRoot = new Border
        {
            Background = CardBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Window,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _cardScale,
            Resources = CreatePreviewResources(),
            Effect = CreateCardEffect(),
            Child = layout,
        };
        _cardRoot.SetResourceReference(Border.BackgroundProperty, CardBackgroundResource);
        _cardRoot.SetResourceReference(Border.BorderBrushProperty, GlassBorderResource);
        _cardRoot.ContextMenu = BuildPreviewContextMenu();
        _cardRoot.ContextMenuOpening += (_, _) => _suppressDeactivatedClose = true;
        _cardRoot.ContextMenuClosing += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => _suppressDeactivatedClose = false), DispatcherPriority.ContextIdle);
        Content = _cardRoot;

        Deactivated += (_, _) =>
        {
            // UI-audit runs are driven by an external automation process. Keep
            // the card available to that process without changing the normal
            // click-away dismissal behavior.
            bool uiAudit = Environment.GetEnvironmentVariable(ToolWindowBase.UiAuditEnvVar) == "1";
            if (_contextSelectionCloseGuard.ShouldClose(
                    _suppressDeactivatedClose || _modalInteractionActive,
                    uiAudit))
            {
                RequestClose();
            }
        }; // click-away dismiss
        Activated += (_, _) =>
        {
            if (!_modalInteractionActive)
            {
                _suppressDeactivatedClose = false;
            }

            _contextSelectionCloseGuard.OnActivated();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        _actions.ActiveContext.PropertyChanged += OnActiveContextChanged;
    }

    /// <summary>Shows (or re-targets) the card for a new preview result.</summary>
    public void Present(FilePreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Title = System.IO.Path.GetFileName(result.FilePath);
        _currentFilePath = result.FilePath;
        _titleText.Text = string.IsNullOrWhiteSpace(Title) ? result.FilePath : Title;
        _subtitleText.Text = result.FilePath;
        _subtitleText.ToolTip = result.FilePath;
        _titleText.ToolTip = result.FilePath;
        PreviewBadgeInfo badge = BuildPreviewBadgeInfo(result);
        _fileKindText.Text = badge.ShortLabel;
        _providerKindText.Text = badge.ProviderLabel;
        _fileKindText.ToolTip = badge.ToolTip;
        _providerKindText.ToolTip = badge.ToolTip;

        _bodyHost.Content = BuildBody(result);
        AutomationProperties.SetName(_bodyHost, AccessibleState(result));
        _inspectorHost.Child = BuildInspector(result);
        UpdateInspectorChrome();

        bool opening = !IsVisible;
        if (opening)
        {
            SizeToOwningMonitor();
            Show();
            ApplyPendingPhysicalBounds();
            Activate();
            PlayOpenAnimation();
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                AutomationPeer? peer = UIElementAutomationPeer.FromElement(_bodyHost) ??
                    UIElementAutomationPeer.CreatePeerForElement(_bodyHost);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }),
            DispatcherPriority.ContextIdle);
    }

    /// <summary>Hides the reusable shell after a successful hand-off to the image viewer.</summary>
    public void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    /// <summary>Refreshes dynamic palette resources without replacing the open card.</summary>
    internal void RefreshTheme()
    {
        if (_isClosing)
        {
            return;
        }

        UpdatePreviewResources(_cardRoot.Resources);
        _cardRoot.Effect = CreateCardEffect();
        foreach (ContextMenu menu in _contextMenus)
        {
            UpdatePreviewResources(menu.Resources);
            menu.HasDropShadow = !SystemParameters.HighContrast;
        }

        if (_currentMarkdownViewer is not null && _currentMarkdownSource is not null)
        {
            RefreshMarkdownTheme(_currentMarkdownViewer, _currentMarkdownSource);
        }
    }

    private void RefreshMarkdownTheme(FlowDocumentScrollViewer viewer, string markdown)
    {
        FlowDocument previousDocument = viewer.Document;
        TextSelection previousSelection = viewer.Selection;
        int selectionStart = previousDocument.ContentStart.GetOffsetToPosition(previousSelection.Start);
        int selectionEnd = previousDocument.ContentStart.GetOffsetToPosition(previousSelection.End);
        ScrollViewer? previousScrollViewer = FindScrollViewer(viewer);
        double horizontalOffset = previousScrollViewer?.HorizontalOffset ?? 0;
        double verticalOffset = previousScrollViewer?.VerticalOffset ?? 0;

        FlowDocument nextDocument = BuildMarkdownDocument(markdown);
        viewer.Document = nextDocument;
        TextPointer? nextStart = nextDocument.ContentStart.GetPositionAtOffset(selectionStart, LogicalDirection.Forward);
        TextPointer? nextEnd = nextDocument.ContentStart.GetPositionAtOffset(selectionEnd, LogicalDirection.Forward);
        if (nextStart is not null && nextEnd is not null)
        {
            viewer.Selection.Select(nextStart, nextEnd);
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                ScrollViewer? currentScrollViewer = FindScrollViewer(viewer);
                currentScrollViewer?.ScrollToHorizontalOffset(horizontalOffset);
                currentScrollViewer?.ScrollToVerticalOffset(verticalOffset);
            }),
            DispatcherPriority.Loaded);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            ScrollViewer? descendant = FindScrollViewer(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    internal static PreviewBadgeInfo BuildPreviewBadgeInfo(FilePreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string extension = System.IO.Path.GetExtension(result.FilePath).TrimStart('.').ToUpperInvariant();
        string shortLabel = ShortExtensionLabel(extension, result.Kind);
        string providerLabel = result.Kind switch
        {
            FilePreviewKind.Csv => "TABLE",
            FilePreviewKind.PlainText => ResolveTextProviderLabel(extension),
            FilePreviewKind.Markdown => "MARKDOWN",
            FilePreviewKind.Image => "IMAGE",
            FilePreviewKind.FileInfo => "FILE",
            FilePreviewKind.Loading => "LOADING",
            _ => "ERROR",
        };

        return new PreviewBadgeInfo(shortLabel, providerLabel, $"{shortLabel} {providerLabel}");
    }

    private static string ShortExtensionLabel(string extension, FilePreviewKind kind)
    {
        if (extension == "MARKDOWN")
        {
            return "MD";
        }

        if (extension == "GITIGNORE")
        {
            return "GIT";
        }

        if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 4)
        {
            return extension;
        }

        return kind switch
        {
            FilePreviewKind.Csv => "CSV",
            FilePreviewKind.PlainText => "TXT",
            FilePreviewKind.Markdown => "MD",
            FilePreviewKind.Image => "IMG",
            FilePreviewKind.FileInfo => "FILE",
            FilePreviewKind.Loading => "…",
            _ => "ERR",
        };
    }

    private static string ResolveTextProviderLabel(string extension)
    {
        if (extension is "MD" or "MARKDOWN")
        {
            return "MARKDOWN";
        }

        if (extension is "JSON")
        {
            return "DATA";
        }

        if (extension is "LOG")
        {
            return "LOG";
        }

        if (extension is "CS" or "JS" or "TS" or "JSX" or "TSX" or "PY" or "RB" or "GO" or "RS" or "JAVA" or
            "C" or "CPP" or "H" or "SQL" or "SH" or "PS1" or "BAT")
        {
            return "CODE";
        }

        if (extension is "HTML" or "HTM" or "CSS")
        {
            return "WEB";
        }

        if (extension is "XML" or "YAML" or "YML" or "TOML" or "INI" or "CFG" or "CSPROJ" or "SLN" or "GITIGNORE")
        {
            return "CONFIG";
        }

        return "TEXT";
    }

    private object BuildBody(FilePreviewResult result)
    {
        _contextMenus.RemoveAll(menu => !ReferenceEquals(menu, _cardRoot.ContextMenu));
        HideCopyContentAction();
        _currentFormattedContent = null;
        _copyFormattedButton.Visibility = Visibility.Collapsed;
        _addToContextButton.Visibility = Visibility.Collapsed;
        _retryButton.Visibility = Visibility.Collapsed;
        _locateButton.Visibility = Visibility.Collapsed;
        _cancelButton.Visibility = Visibility.Collapsed;
        _currentImage = null;
        _currentImageScroll = null;
        _currentMarkdownViewer = null;
        _currentMarkdownSource = null;
        _csvView = null;
        _csvModel = null;
        _sortColumn = -1;
        ConfigureImageActions(result);
        ConfigureRecoveryActions(result);

        bool supportsContext = result.Kind is FilePreviewKind.Csv or
            FilePreviewKind.PlainText or FilePreviewKind.Markdown;
        if (supportsContext)
        {
            _addToContextButton.Visibility = Visibility.Visible;
            UpdateActiveContextAction();
        }

        if (result.SourceByteLength == 0 && result.Kind != FilePreviewKind.Loading)
        {
            _filterHost.Visibility = Visibility.Collapsed;
            return WithNotices(result, BuildEmptyBody());
        }

        switch (result.Kind)
        {
            case FilePreviewKind.Csv when result.Csv is not null:
                _filterBox.Text = string.Empty;
                _filterHost.Visibility = Visibility.Visible;
                object csvBody = BuildCsvBody(result.Csv);
                string copyLabel = result.Csv.IsSampled
                    ? $"Copy {result.Csv.Rows.Count:N0}-row sample"
                    : "Copy table";
                ConfigureCopyContentAction(
                    copyLabel,
                    result.Csv.IsSampled
                        ? "Copy the visible sample as tab-separated text; rows beyond the sample are not included"
                        : "Copy the visible rows as tab-separated text",
                    content: null,
                    copyCsvFromView: true);
                return WithNotices(result, csvBody);

            case FilePreviewKind.PlainText:
                _filterHost.Visibility = Visibility.Collapsed;
                ConfigureCopyContentAction(
                    "Copy original",
                    "Copy the original source without Octadock notices",
                    PreviewClipboardContent.ForResult(result));
                ConfigureFormattedCopyAction(result);
                return WithNotices(
                    result,
                    BuildTextBody(result.PresentationContent ?? string.Empty));

            case FilePreviewKind.Markdown:
                _filterHost.Visibility = Visibility.Collapsed;
                ConfigureCopyContentAction(
                    "Copy original",
                    "Copy the original Markdown source without Octadock notices",
                    PreviewClipboardContent.ForResult(result));
                return WithNotices(
                    result,
                    BuildMarkdownBody(result.SourceContent ?? result.Text ?? string.Empty));

            case FilePreviewKind.Image when result.ImagePath is not null:
                _filterHost.Visibility = Visibility.Collapsed;
                return WithNotices(result, BuildImageBody(result));

            case FilePreviewKind.FileInfo:
                _filterHost.Visibility = Visibility.Collapsed;
                ConfigureCopyContentAction(
                    "Copy info",
                    "Copy the file information",
                    PreviewClipboardContent.ForResult(result));
                return WithNotices(result, BuildFileInfoBody(result.Text ?? string.Empty));

            case FilePreviewKind.Loading:
                _filterHost.Visibility = Visibility.Collapsed;
                _cancelButton.Visibility = Visibility.Visible;
                return BuildLoadingBody();

            default:
                _filterHost.Visibility = Visibility.Collapsed;
                return BuildErrorBody(result.Error ?? "Preview failed.");
        }
    }

    private void ConfigureRecoveryActions(FilePreviewResult result)
    {
        if (result.Failure is not { } failure)
        {
            return;
        }

        _retryButton.Visibility = failure.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        _locateButton.Visibility = failure.CanLocate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureFormattedCopyAction(FilePreviewResult result)
    {
        _currentFormattedContent = PreviewClipboardContent.FormattedForResult(result);
        if (string.IsNullOrEmpty(_currentFormattedContent))
        {
            _copyFormattedButton.Visibility = Visibility.Collapsed;
            return;
        }

        SetActionButtonLabel(_copyFormattedButton, "Copy formatted JSON");
        SetActionButtonHelp(
            _copyFormattedButton,
            "Copy the explicitly formatted JSON (original source is unchanged)");
        _copyFormattedButton.Visibility = Visibility.Visible;
    }

    private static object WithNotices(FilePreviewResult result, object body)
    {
        var messages = new List<string>();
        if (result.Kind != FilePreviewKind.Error && result.Failure is { } failure)
        {
            messages.Add(failure.Message);
        }

        messages.AddRange(result.Warnings
            .Where(warning => warning.Kind != FilePreviewWarningKind.Empty)
            .Select(warning => warning.Message));
        if (messages.Count == 0 || body is not UIElement element)
        {
            return body;
        }

        var notice = WithResource(
            new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 8, 14, 8),
                Child = WithResource(
                    new TextBlock
                    {
                        Text = string.Join("  ", messages),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    (TextBlock.ForegroundProperty, TextResource)),
            },
            (Border.BackgroundProperty, ActiveActionResource),
            (Border.BorderBrushProperty, RuleResource));
        AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(notice, string.Join(" ", messages));

        var panel = new DockPanel();
        DockPanel.SetDock(notice, Dock.Top);
        panel.Children.Add(notice);
        panel.Children.Add(element);
        return panel;
    }

    private UIElement BuildInspector(FilePreviewResult result)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(14, 13, 14, 14),
        };

        panel.Children.Add(MakeInspectorHeader(result));
        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = "DETAILS",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            },
            (TextBlock.ForegroundProperty, AccentResource)));

        foreach (PreviewInspectorRow row in PreviewInspectorModel.BuildRows(result))
        {
            panel.Children.Add(MakeInspectorRow(row));
        }

        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
        };
    }

    private UIElement MakeInspectorHeader(FilePreviewResult result)
    {
        PreviewBadgeInfo badge = BuildPreviewBadgeInfo(result);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badgeFrame = WithResource(
            new Border
            {
                MinWidth = 42,
                Height = 34,
                Padding = new Thickness(7, 0, 7, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = OctadockDesignTokens.Radius.Control,
                Child = WithResource(
                    new TextBlock
                    {
                        Text = badge.ShortLabel,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    (TextBlock.ForegroundProperty, TextResource)),
            },
            (Border.BackgroundProperty, ActiveActionResource),
            (Border.BorderBrushProperty, GlassBorderResource));

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Children =
            {
                WithResource(
                    new TextBlock
                    {
                        Text = badge.ProviderLabel,
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                    (TextBlock.ForegroundProperty, TextResource)),
                WithResource(
                    new TextBlock
                    {
                        Text = "Preview details",
                        FontSize = 10,
                        Margin = new Thickness(0, 2, 0, 0),
                    },
                    (TextBlock.ForegroundProperty, MutedTextResource)),
            },
        };

        Grid.SetColumn(badgeFrame, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(badgeFrame);
        grid.Children.Add(text);
        return grid;
    }

    private static UIElement MakeInspectorRow(PreviewInspectorRow row)
    {
        return new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 11),
            Children =
            {
                WithResource(
                    new TextBlock
                    {
                        Text = row.Label.ToUpperInvariant(),
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 3),
                    },
                    (TextBlock.ForegroundProperty, MutedTextResource)),
                WithResource(
                    new TextBlock
                    {
                        Text = row.Value,
                        FontSize = 12,
                        TextWrapping = row.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        TextTrimming = row.Wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                    },
                    (TextBlock.ForegroundProperty, TextResource)),
            },
        };
    }

    // ---- CSV body ----------------------------------------------------------

    private object BuildCsvBody(CsvPreviewModel model)
    {
        _csvModel = model;

        var gridView = new GridView { AllowsColumnReorder = false };
        for (int i = 0; i < model.Columns.Count; i++)
        {
            gridView.Columns.Add(BuildColumn(model.Columns[i], i));
        }

        var listView = new ListView
        {
            ItemsSource = model.Rows,
            View = gridView,
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            BorderThickness = new Thickness(0),
            ItemContainerStyle = CreateListViewItemStyle(),
            Margin = new Thickness(10, 6, 10, 0),
        };
        listView.SetResourceReference(Control.ForegroundProperty, TextResource);

        // Let the ListView's built-in ScrollViewer own both axes (wrapping it in
        // an outer ScrollViewer would defeat UI virtualization on large tables).
        ScrollViewer.SetHorizontalScrollBarVisibility(listView, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(listView, ScrollBarVisibility.Auto);

        // Header clicks bubble up as ButtonBase.Click (GridViewColumnHeader is a ButtonBase).
        listView.AddHandler(
            System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
            new RoutedEventHandler(OnColumnHeaderClick));

        // ItemsSource sets the ListView's default view; filtering/sorting it here
        // is reflected directly in the ListView.
        _csvView = CollectionViewSource.GetDefaultView(model.Rows);
        _csvView.Filter = FilterRow;

        var body = new DockPanel();
        UIElement? stats = BuildStatsFooter(model);
        if (stats is not null)
        {
            DockPanel.SetDock(stats, Dock.Bottom);
            body.Children.Add(stats);
        }

        body.Children.Add(listView);
        return WrapBodyFrame(body);
    }

    private static GridViewColumn BuildColumn(CsvColumn column, int index)
    {
        bool rightAlign = column.Type == CsvColumnType.Number;
        var header = new GridViewColumnHeader
        {
            Content = column.Name,
            Tag = index,
            HorizontalContentAlignment = rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };

        var cellFactory = new FrameworkElementFactory(typeof(TextBlock));
        cellFactory.SetBinding(TextBlock.TextProperty, new Binding($"[{index}]"));
        cellFactory.SetBinding(
            TextBlock.ForegroundProperty,
            new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(
                    RelativeSourceMode.FindAncestor,
                    typeof(ListViewItem),
                    ancestorLevel: 1),
            });
        cellFactory.SetValue(
            FrameworkElement.HorizontalAlignmentProperty,
            rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        if (rightAlign)
        {
            cellFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            cellFactory.SetValue(
                TextBlock.FontFamilyProperty,
                new FontFamily("Consolas, Cascadia Mono, Courier New"));
        }

        return new GridViewColumn
        {
            Header = header,
            CellTemplate = new DataTemplate { VisualTree = cellFactory },
        };
    }

    private bool FilterRow(object item)
    {
        string text = _filterBox.Text;
        if (string.IsNullOrEmpty(text) || item is not string[] cells)
        {
            return true;
        }

        foreach (string cell in cells)
        {
            if (cell is not null && cell.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        GridViewColumnHeader? header = ResolveHeader(e.OriginalSource as DependencyObject);
        if (_csvView is null || header?.Tag is not int column)
        {
            return;
        }

        if (_sortColumn == column)
        {
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _sortColumn = column;
            _sortDirection = ListSortDirection.Ascending;
        }

        bool numeric = _csvModel is not null &&
            column < _csvModel.Columns.Count &&
            _csvModel.Columns[column].Type == CsvColumnType.Number;

        if (_csvView is ListCollectionView list)
        {
            list.CustomSort = new RowComparer(column, _sortDirection, numeric);
        }
    }

    /// <summary>Walks up from the click's original source to the owning column header, if any.</summary>
    private static GridViewColumnHeader? ResolveHeader(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is GridViewColumnHeader header)
            {
                return header;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    /// <summary>
    /// Stats for the first numeric column (count/sum/mean/min/max), formatted
    /// with the invariant culture. Returns null when no column is numeric.
    /// </summary>
    private static UIElement? BuildStatsFooter(CsvPreviewModel model)
    {
        int col = -1;
        for (int i = 0; i < model.Columns.Count; i++)
        {
            if (model.Columns[i].Type == CsvColumnType.Number)
            {
                col = i;
                break;
            }
        }

        if (col < 0)
        {
            return null;
        }

        long count = 0;
        double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (string[] row in model.Rows)
        {
            if (col >= row.Length || string.IsNullOrWhiteSpace(row[col]))
            {
                continue;
            }

            string raw = row[col].Trim();
            if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double value) &&
                !double.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            {
                continue;
            }

            count++;
            sum += value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 8, 14, 10),
        };
        if (model.IsSampled)
        {
            footer.Children.Add(StatLabel("SAMPLE STATISTICS", SecondaryAccentResource));
        }

        footer.Children.Add(StatLabel(model.Columns[col].Name, AccentResource));
        if (count == 0)
        {
            footer.Children.Add(StatLabel("no numeric values", MutedTextResource));
            return WrapFooter(footer);
        }

        double mean = sum / count;
        footer.Children.Add(Stat("count", count.ToString(CultureInfo.InvariantCulture)));
        footer.Children.Add(Stat("sum", sum.ToString("0.######", CultureInfo.InvariantCulture)));
        footer.Children.Add(Stat("mean", mean.ToString("0.######", CultureInfo.InvariantCulture)));
        footer.Children.Add(Stat("min", min.ToString("0.######", CultureInfo.InvariantCulture)));
        footer.Children.Add(Stat("max", max.ToString("0.######", CultureInfo.InvariantCulture)));
        return WrapFooter(footer);
    }

    private static Border WrapFooter(UIElement content)
        => WithResource(
            new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = content,
            },
            (Border.BorderBrushProperty, RuleResource));

    private static TextBlock StatLabel(string text, string foregroundResource)
        => WithResource(
            new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
            },
            (TextBlock.ForegroundProperty, foregroundResource));

    private static UIElement Stat(string label, string value)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = label + " ",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            },
            (TextBlock.ForegroundProperty, MutedTextResource)));
        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = value,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                VerticalAlignment = VerticalAlignment.Center,
            },
            (TextBlock.ForegroundProperty, TextResource)));
        return panel;
    }

    private static Border WrapBodyFrame(UIElement content)
    {
        var frame = new Grid();
        frame.Children.Add(content);
        frame.Children.Add(WithResource(
            new Border
            {
                Height = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = 0.72,
                IsHitTestVisible = false,
            },
            (Border.BackgroundProperty, AccentResource)));
        frame.Children.Add(WithResource(
            new Border
            {
                Width = 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = 0.42,
                IsHitTestVisible = false,
            },
            (Border.BackgroundProperty, SecondaryAccentResource)));

        return WithResource(
            new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = OctadockDesignTokens.Radius.Panel,
                Margin = new Thickness(14, 12, 14, 14),
                ClipToBounds = true,
                Child = frame,
            },
            (Border.BackgroundProperty, PanelBackgroundResource),
            (Border.BorderBrushProperty, RuleResource));
    }

    // ---- Plain-text body ---------------------------------------------------

    private UIElement BuildTextBody(string text)
    {
        var editor = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            SelectionBrush = AccentBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18, 14, 18, 16),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            ContextMenu = BuildPreviewContextMenu(),
        };
        editor.SetResourceReference(Control.ForegroundProperty, TextResource);
        editor.SetResourceReference(TextBox.SelectionBrushProperty, AccentResource);
        AutomationProperties.SetName(editor, "Preview source content");
        AutomationProperties.SetHelpText(
            editor,
            "Read-only source content. Select text here or use Copy original.");

        return WrapBodyFrame(editor);
    }

    // ---- Markdown body -------------------------------------------------------

    /// <summary>Rendered markdown (headings/lists/code/quotes); links show their URL as a tooltip only.</summary>
    private UIElement BuildMarkdownBody(string markdown)
    {
        _currentMarkdownSource = markdown;
        _currentMarkdownViewer = new FlowDocumentScrollViewer
        {
            Document = BuildMarkdownDocument(markdown),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsToolBarVisible = false,
            ContextMenu = BuildPreviewContextMenu(),
        };

        return WrapBodyFrame(_currentMarkdownViewer);
    }

    private static FlowDocument BuildMarkdownDocument(string markdown)
    {
        var palette = new MarkdownPalette(
            TextBrush,
            MutedBrush,
            AccentBrush,
            FieldBackground,
            HeaderRule);
        return MarkdownRendering.BuildDocument(markdown, palette);
    }

    private static UIElement BuildLoadingBody()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(28),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 260,
        };
        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = "Loading preview…",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
            },
            (TextBlock.ForegroundProperty, TextResource)));
        panel.Children.Add(WithResource(
            new ProgressBar
            {
                IsIndeterminate = true,
                Height = 3,
                MinWidth = 240,
                BorderThickness = new Thickness(0),
            },
            (Control.ForegroundProperty, AccentResource),
            (Control.BackgroundProperty, FieldBackgroundResource)));

        var frame = WithResource(
            new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = OctadockDesignTokens.Radius.Panel,
                Margin = new Thickness(22),
                Child = panel,
            },
            (Border.BackgroundProperty, PanelBackgroundResource),
            (Border.BorderBrushProperty, RuleResource));
        AutomationProperties.SetName(frame, "Loading preview");
        AutomationProperties.SetLiveSetting(frame, AutomationLiveSetting.Polite);
        return frame;
    }

    private static UIElement BuildEmptyBody()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(28),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 520,
        };
        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = "0 bytes—nothing to preview",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
            },
            (TextBlock.ForegroundProperty, TextResource)));
        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = "The file is valid but contains no content.",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 7, 0, 0),
            },
            (TextBlock.ForegroundProperty, MutedTextResource)));

        var frame = WithResource(
            new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = OctadockDesignTokens.Radius.Panel,
                Margin = new Thickness(22),
                Child = panel,
            },
            (Border.BackgroundProperty, PanelBackgroundResource),
            (Border.BorderBrushProperty, RuleResource));
        AutomationProperties.SetName(frame, "0 bytes—nothing to preview");
        AutomationProperties.SetLiveSetting(frame, AutomationLiveSetting.Polite);
        return frame;
    }

    private static UIElement BuildErrorBody(string message)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 560,
        };

        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = "Preview unavailable",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
            },
            (TextBlock.ForegroundProperty, TextResource)));

        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
            },
            (TextBlock.ForegroundProperty, MutedTextResource)));

        return WithResource(
            new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = OctadockDesignTokens.Radius.Panel,
                Margin = new Thickness(22),
                Child = panel,
            },
            (Border.BackgroundProperty, PanelBackgroundResource),
            (Border.BorderBrushProperty, RuleResource));
    }

    // ---- Image body --------------------------------------------------------

    /// <summary>
    /// Decodes the image once (OnLoad so the file is not locked; IgnoreImageCache
    /// so a re-preview of an edited file is fresh), caps the decode width to keep
    /// huge photos cheap, and appends the pixel dimensions to the header.
    /// </summary>
    private UIElement BuildImageBody(FilePreviewResult result)
    {
        string imagePath = result.ImagePath
            ?? throw new ArgumentException("An image preview requires an image path.", nameof(result));

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = 1600;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            // The display bitmap is capped at 1600px. Source dimensions come from
            // the provider's off-UI metadata pass so the header and inspector never
            // misreport the decoded preview size as the original image size.
            if (result.ImagePixelWidth is > 0 && result.ImagePixelHeight is > 0)
            {
                _titleText.Text =
                    $"{Title}  —  {result.ImagePixelWidth.Value} × {result.ImagePixelHeight.Value}";
            }

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 10, 14, 12),
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var scroll = new ScrollViewer
            {
                Content = image,
                Background = Brushes.Transparent,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                PanningMode = PanningMode.Both,
                ContextMenu = BuildPreviewContextMenu(),
            };
            scroll.SizeChanged += (_, _) => UpdateFittedImageBounds();

            _currentImage = image;
            _currentImageScroll = scroll;
            ApplyImageFitMode();

            return WrapBodyFrame(scroll);
        }
        catch (Exception)
        {
            _currentImage = null;
            _currentImageScroll = null;
            _fitImageButton.Visibility = Visibility.Collapsed;
            return BuildErrorBody("The image could not be displayed safely. Try again or open its folder.");
        }
    }

    // ---- File-info fallback body -------------------------------------------

    /// <summary>The unsupported-type fallback: metadata lines for a file without a built-in preview.</summary>
    private UIElement BuildFileInfoBody(string details)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(22, 18, 22, 20),
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = "There's no built-in preview for this file type.",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            },
            (TextBlock.ForegroundProperty, TextResource)));

        panel.Children.Add(WithResource(
            new TextBlock
            {
                Text = "You can still show it in File Explorer, copy its path, or open it with the default app.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
            },
            (TextBlock.ForegroundProperty, MutedTextResource)));

        var rows = new StackPanel();
        foreach ((string label, string value) in ParseFileInfoRows(details))
        {
            rows.Children.Add(MakeInfoRow(label, value));
        }

        panel.Children.Add(rows);

        return WithResource(
            new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = OctadockDesignTokens.Radius.Panel,
                Margin = new Thickness(20),
                Child = panel,
                ContextMenu = BuildPreviewContextMenu(),
            },
            (Border.BackgroundProperty, PanelBackgroundResource),
            (Border.BorderBrushProperty, RuleResource));
    }

    private static IEnumerable<(string Label, string Value)> ParseFileInfoRows(string details)
    {
        foreach (string rawLine in details.Split(
            [Environment.NewLine],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int splitAt = rawLine.IndexOf("  ", StringComparison.Ordinal);
            if (splitAt > 0)
            {
                string label = rawLine[..splitAt].Trim();
                string value = rawLine[splitAt..].Trim();
                yield return (label, value);
            }
            else
            {
                yield return ("Info", rawLine);
            }
        }
    }

    private static UIElement MakeInfoRow(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label.ToUpperInvariant(),
            Foreground = MutedBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 14, 0),
        };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, MutedTextResource);

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = TextBrush,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            TextWrapping = TextWrapping.Wrap,
        };
        valueBlock.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);
        return row;
    }

    // ---- Sizing / chrome / animation --------------------------------------

    private void OnDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // WPF can throw when the mouse is released during drag startup.
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        _actions.ActiveContext.PropertyChanged -= OnActiveContextChanged;
        base.OnClosing(e);
    }

    private void RequestClose()
    {
        if (_isClosing)
        {
            return;
        }

        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            _isClosing = true;
        }
    }

    private Border MakePreviewBadge()
    {
        var labelStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 5, 6, 6),
            Children =
            {
                _fileKindText,
                _providerKindText,
            },
        };

        var frame = new Grid
        {
            Children =
            {
                WithResource(
                    new Border
                    {
                        Height = 2,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(8, 0, 8, 7),
                        Opacity = 0.9,
                    },
                    (Border.BackgroundProperty, SecondaryAccentResource)),
                labelStack,
            },
        };

        return WithResource(
            new Border
            {
                Width = 72,
                Height = 44,
                Margin = new Thickness(0, 0, 14, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = OctadockDesignTokens.Radius.Rail,
                Child = frame,
            },
            (Border.BackgroundProperty, PanelBackgroundResource),
            (Border.BorderBrushProperty, GlassBorderResource));
    }

    private static Style CreateListViewItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TextResource)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(RowHoverResource)));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(HighlightTextResource)));
        style.Triggers.Add(hover);

        var selected = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(RowSelectedResource)));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(HighlightTextResource)));
        style.Triggers.Add(selected);
        return style;
    }

    private static ResourceDictionary CreatePreviewResources()
    {
        var resources = (ResourceDictionary)XamlReader.Parse(
            """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style x:Key="PreviewScrollRepeatButton" TargetType="{x:Type RepeatButton}">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="Focusable" Value="False"/>
                <Setter Property="IsTabStop" Value="False"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="{x:Type RepeatButton}">
                      <Border Background="Transparent"/>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>

              <Style TargetType="{x:Type ScrollBar}">
                <Setter Property="Stylus.IsFlicksEnabled" Value="False"/>
                <Setter Property="Foreground" Value="{DynamicResource Preview.ScrollThumb}"/>
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="Width" Value="10"/>
                <Setter Property="MinWidth" Value="10"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="{x:Type ScrollBar}">
                      <Grid Background="Transparent" Margin="2">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                          <Track.DecreaseRepeatButton>
                            <RepeatButton x:Name="DecreaseButton" Style="{StaticResource PreviewScrollRepeatButton}" Command="{x:Static ScrollBar.PageUpCommand}"/>
                          </Track.DecreaseRepeatButton>
                          <Track.Thumb>
                            <Thumb>
                              <Thumb.Template>
                                <ControlTemplate TargetType="{x:Type Thumb}">
                                  <Border MinWidth="6"
                                          MinHeight="6"
                                          CornerRadius="4"
                                          Background="{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ScrollBar}}}"/>
                                </ControlTemplate>
                              </Thumb.Template>
                            </Thumb>
                          </Track.Thumb>
                          <Track.IncreaseRepeatButton>
                            <RepeatButton x:Name="IncreaseButton" Style="{StaticResource PreviewScrollRepeatButton}" Command="{x:Static ScrollBar.PageDownCommand}"/>
                          </Track.IncreaseRepeatButton>
                        </Track>
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property="Orientation" Value="Horizontal">
                          <Setter Property="Width" Value="Auto"/>
                          <Setter Property="MinWidth" Value="0"/>
                          <Setter Property="Height" Value="10"/>
                          <Setter Property="MinHeight" Value="10"/>
                          <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/>
                          <Setter TargetName="DecreaseButton" Property="Command" Value="{x:Static ScrollBar.PageLeftCommand}"/>
                          <Setter TargetName="IncreaseButton" Property="Command" Value="{x:Static ScrollBar.PageRightCommand}"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                          <Setter Property="Foreground" Value="{DynamicResource Preview.ScrollThumbHover}"/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>

              <Style TargetType="{x:Type GridViewColumnHeader}">
                <Setter Property="Foreground" Value="{DynamicResource Preview.ColumnHeaderText}"/>
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="BorderThickness" Value="0"/>
                <Setter Property="Padding" Value="8,8,8,7"/>
                <Setter Property="FontSize" Value="11"/>
                <Setter Property="FontWeight" Value="SemiBold"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="{x:Type GridViewColumnHeader}">
                      <Border x:Name="Root"
                              Background="{TemplateBinding Background}"
                              BorderBrush="{DynamicResource Preview.ColumnBorder}"
                              BorderThickness="0,0,1,1"
                              Padding="{TemplateBinding Padding}"
                              TextElement.Foreground="{TemplateBinding Foreground}">
                        <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                          RecognizesAccessKey="True"/>
                      </Border>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                          <Setter TargetName="Root" Property="Background" Value="{DynamicResource Preview.ColumnHover}"/>
                          <Setter Property="Foreground" Value="{DynamicResource Preview.HighlightText}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                          <Setter TargetName="Root" Property="Background" Value="{DynamicResource Preview.ColumnPressed}"/>
                          <Setter Property="Foreground" Value="{DynamicResource Preview.HighlightText}"/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
            </ResourceDictionary>
            """);
        UpdatePreviewResources(resources);
        return resources;
    }

    private static Effect? CreateCardEffect()
        => SystemParameters.HighContrast
            ? null
            : new DropShadowEffect
            {
                BlurRadius = 36,
                Direction = 270,
                Opacity = 0.36,
                ShadowDepth = 12,
                Color = Colors.Black,
            };

    private static void UpdatePreviewResources(ResourceDictionary resources)
    {
        PreviewCardPalette palette = CurrentPalette;
        resources[CardBackgroundResource] = palette.CardBackground;
        resources[ChromeBackgroundResource] = palette.ChromeBackground;
        resources[PanelBackgroundResource] = palette.PanelBackground;
        resources[GlassBorderResource] = palette.GlassBorder;
        resources[TextResource] = palette.Text;
        resources[MutedTextResource] = palette.MutedText;
        resources[AccentResource] = palette.Accent;
        resources[SecondaryAccentResource] = palette.SecondaryAccent;
        resources[FieldBackgroundResource] = palette.FieldBackground;
        resources[RuleResource] = palette.Rule;
        resources[MenuBackgroundResource] = palette.MenuBackground;
        resources[MenuHoverResource] = palette.Hover;
        resources[DangerHoverResource] = palette.DangerHover;
        resources[ActiveActionResource] = ActiveActionBackground;
        resources[ActionHoverResource] = ActionHoverBackground;
        resources[RowHoverResource] = palette.RowHover;
        resources[RowSelectedResource] = palette.RowSelected;
        resources[ScrollThumbResource] = palette.ScrollThumb;
        resources[ScrollThumbHoverResource] = palette.ScrollThumbHover;
        resources[ColumnHeaderTextResource] = palette.ColumnHeaderText;
        resources[ColumnBorderResource] = palette.ColumnBorder;
        resources[ColumnHoverResource] = palette.ColumnHover;
        resources[ColumnPressedResource] = palette.ColumnPressed;
        resources[HighlightTextResource] = SystemParameters.HighContrast
            ? SystemColors.HighlightTextBrush
            : palette.ColumnHeaderText;
        resources[MenuHighlightTextResource] = SystemParameters.HighContrast
            ? SystemColors.HighlightTextBrush
            : palette.Text;
    }

    private ContextMenu BuildPreviewContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = MenuBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            HasDropShadow = !SystemParameters.HighContrast,
            SnapsToDevicePixels = true,
        };
        UpdatePreviewResources(menu.Resources);
        _contextMenus.Add(menu);
        menu.SetResourceReference(Control.BackgroundProperty, MenuBackgroundResource);
        menu.SetResourceReference(Control.BorderBrushProperty, GlassBorderResource);
        menu.Resources.Add(typeof(MenuItem), CreateMenuItemStyle());
        menu.Opened += (_, _) =>
        {
            _suppressDeactivatedClose = true;
            PopulatePreviewContextMenu(menu);
        };
        menu.Closed += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => _suppressDeactivatedClose = false), DispatcherPriority.ContextIdle);
        return menu;
    }

    private void PopulatePreviewContextMenu(ContextMenu menu)
    {
        menu.Items.Clear();

        if (_copyContentButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuItem(GetActionButtonLabel(_copyContentButton), CopyCurrentContent));
        }

        if (_copyFormattedButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuItem(GetActionButtonLabel(_copyFormattedButton), CopyFormattedContent));
        }

        if (_copyContentButton.Visibility == Visibility.Visible ||
            _copyFormattedButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuSeparator());
        }

        if (_addToContextButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuItem(
                GetActionButtonLabel(_addToContextButton),
                () =>
                {
                    // Let the ContextMenu close and reactivate its owner before
                    // beginning the longer Context-window transition.
                    menu.IsOpen = false;
                    Dispatcher.BeginInvoke(
                        new Action(AddCurrentFileToContext),
                        DispatcherPriority.ContextIdle);
                }));
            menu.Items.Add(MakeMenuSeparator());
        }

        if (_addToShelfButton.Visibility == Visibility.Visible || _fitImageButton.Visibility == Visibility.Visible)
        {
            if (_fitImageButton.Visibility == Visibility.Visible)
            {
                menu.Items.Add(MakeMenuItem(GetActionButtonLabel(_fitImageButton), ToggleImageFitMode));
            }

            if (_addToShelfButton.Visibility == Visibility.Visible)
            {
                menu.Items.Add(MakeMenuItem("Add to dock", () => InvokePathAction(_actions.AddToShelf)));
            }

            menu.Items.Add(MakeMenuSeparator());
        }

        if (_retryButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuItem("Retry preview", () => InvokePathAction(_actions.Retry)));
        }

        if (_locateButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuItem("Locate file…", () => InvokeModalPathAction(_actions.Locate)));
        }

        if (_retryButton.Visibility == Visibility.Visible || _locateButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuSeparator());
        }

        if (_cancelButton.Visibility == Visibility.Visible)
        {
            menu.Items.Add(MakeMenuItem("Cancel loading", _actions.Cancel));
            menu.Items.Add(MakeMenuSeparator());
        }

        menu.Items.Add(MakeMenuItem("Copy path", () => InvokePathAction(_actions.CopyPath)));
        menu.Items.Add(MakeMenuItem("Save a copy as...", () => InvokeModalPathAction(_actions.SaveCopyAs)));
        menu.Items.Add(MakeMenuItem("Show in File Explorer", () => InvokePathAction(_actions.RevealInExplorer)));
        menu.Items.Add(MakeMenuItem("Open externally...", ConfirmOpenWithDefaultApp));
        menu.Items.Add(MakeMenuSeparator());
        menu.Items.Add(MakeMenuItem("Close preview", RequestClose));
    }

    private static Style CreateMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(TextResource)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 184.0));

        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Bd";
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, OctadockDesignTokens.Radius.Small);
        border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Header") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(MenuHoverResource), "Bd"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(MenuHighlightTextResource)));
        template.Triggers.Add(hover);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static MenuItem MakeMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static Border MakeMenuSeparator()
        => WithResource(
            new Border
            {
                Height = 1,
                Margin = new Thickness(8, 5, 8, 5),
            },
            (Border.BackgroundProperty, RuleResource));

    private static string GetActionButtonLabel(Button button)
        => button.Tag?.ToString() ?? "Copy";

    /// <summary>Sizes the Quick Look card on the active monitor in physical pixels.</summary>
    private void SizeToOwningMonitor()
    {
        var monitors = App.Services.GetRequiredService<IMonitorService>();
        DisplayInfo monitor = monitors.GetActiveMonitor();
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        PixelRect physicalBounds = CalculatePreviewPhysicalBounds(monitor.WorkArea, scale);
        Size minimumSize = CalculatePreviewMinimumSize(physicalBounds, scale);
        _pendingPhysicalBounds = physicalBounds;

        // Keep WPF's WM_GETMINMAXINFO contract inside the already-clamped native
        // rectangle. Fixed minima would make SetWindowPos overflow small monitors.
        MinWidth = minimumSize.Width;
        MinHeight = minimumSize.Height;
        Width = physicalBounds.Width / scale;
        Height = physicalBounds.Height / scale;

        // This DIP-space placement is only an initial hint. Present() follows it
        // with SetWindowPos after the HWND exists so negative mixed-DPI monitors
        // do not inherit the primary monitor's coordinate scaling.
        Left = (physicalBounds.X - monitor.Bounds.X) / scale + monitor.Bounds.X;
        Top = (physicalBounds.Y - monitor.Bounds.Y) / scale + monitor.Bounds.Y;
    }

    private void ApplyPendingPhysicalBounds()
    {
        if (_pendingPhysicalBounds is not { } bounds || Hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.PositionPhysicalTopmost(Hwnd, bounds);
        _pendingPhysicalBounds = null;
    }

    internal static Rect CalculatePreviewWindowBounds(Rect workArea)
    {
        const double margin = 24;
        double availableWidth = Math.Max(1, workArea.Width - margin);
        double availableHeight = Math.Max(1, workArea.Height - margin);
        double width = Math.Min(980, Math.Max(DefaultPreviewMinWidth, workArea.Width * 0.58));
        double height = Math.Min(640, Math.Max(DefaultPreviewMinHeight, workArea.Height * 0.62));
        width = Math.Min(width, availableWidth);
        height = Math.Min(height, availableHeight);
        return new Rect(
            workArea.Left + ((workArea.Width - width) / 2),
            workArea.Top + ((workArea.Height - height) / 2),
            width,
            height);
    }

    internal static PixelRect CalculatePreviewPhysicalBounds(PixelRect workArea, double dpiScale)
    {
        double scale = dpiScale <= 0 ? 1.0 : dpiScale;
        Rect dipBounds = CalculatePreviewWindowBounds(new Rect(
            0,
            0,
            workArea.Width / scale,
            workArea.Height / scale));
        int margin = Math.Max(1, (int)Math.Round(24 * scale));
        int topMargin = Math.Max(1, (int)Math.Round(40 * scale));
        int width = Math.Min(
            Math.Max(1, workArea.Width - margin),
            Math.Max(1, (int)Math.Round(dipBounds.Width * scale)));
        int height = Math.Min(
            Math.Max(1, workArea.Height - margin),
            Math.Max(1, (int)Math.Round(dipBounds.Height * scale)));
        return new PixelRect(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + Math.Min(topMargin, Math.Max(0, workArea.Height - height)),
            width,
            height);
    }

    internal static Size CalculatePreviewMinimumSize(PixelRect physicalBounds, double dpiScale)
    {
        double scale = dpiScale <= 0 ? 1.0 : dpiScale;
        return new Size(
            Math.Min(DefaultPreviewMinWidth, Math.Max(1, physicalBounds.Width / scale)),
            Math.Min(DefaultPreviewMinHeight, Math.Max(1, physicalBounds.Height / scale)));
    }

    private void PlayOpenAnimation()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _cardRoot.BeginAnimation(OpacityProperty, null);
            _cardScale.ScaleX = 1;
            _cardScale.ScaleY = 1;
            _cardRoot.Opacity = 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _cardScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, OpenDuration) { EasingFunction = ease });
        _cardScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, OpenDuration) { EasingFunction = ease });
        _cardRoot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, OpenDuration) { EasingFunction = ease });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestClose();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && _fitImageButton.Visibility == Visibility.Visible)
        {
            ToggleImageFitMode();
            e.Handled = true;
        }
    }

    private void ToggleInspector()
    {
        _inspectorVisible = !_inspectorVisible;
        UpdateInspectorChrome();
    }

    private void UpdateInspectorChrome()
    {
        PreviewInspectorChrome chrome = GetPreviewInspectorChrome(_inspectorVisible);
        _inspectorHost.Visibility = chrome.Visibility;
        SetActionActive(_toggleInspectorButton, _inspectorVisible);
        SetActionButtonHelp(_toggleInspectorButton, chrome.ToolTip);
        SetActionButtonLabel(_toggleInspectorButton, chrome.MenuLabel);
    }

    internal static PreviewInspectorChrome GetPreviewInspectorChrome(bool visible)
        => visible
            ? new PreviewInspectorChrome(Visibility.Visible, "Hide details", "Hide details")
            : new PreviewInspectorChrome(Visibility.Collapsed, "Show details", "Show details");

    private void HideCopyContentAction()
    {
        _currentCopyContent = null;
        _copyCsvFromView = false;
        _copyContentButton.Visibility = Visibility.Collapsed;
    }

    private void OnActiveContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ActiveContextState.AddActionLabel) and
            not nameof(ActiveContextState.ActivePackageId) and
            not nameof(ActiveContextState.ActivePackageName))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateActiveContextAction, DispatcherPriority.DataBind);
            return;
        }

        UpdateActiveContextAction();
    }

    private void UpdateActiveContextAction()
    {
        string label = _actions.ActiveContext.AddActionLabel;
        _addToContextText.Text = label;
        SetActionButtonLabel(_addToContextButton, label);
        SetActionButtonHelp(
            _addToContextButton,
            _actions.ActiveContext.HasActiveContext
                ? $"Add this file to {_actions.ActiveContext.ActivePackageName}"
                : "Choose or create the Context that should receive this file");
    }

    private void ConfigureImageActions(FilePreviewResult result)
    {
        bool hasRenderedImage = result.Kind == FilePreviewKind.Image &&
            !string.IsNullOrWhiteSpace(result.ImagePath);
        Visibility imageFileVisibility = hasRenderedImage
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (hasRenderedImage)
        {
            _fitImageToCard = true;
        }

        _addToShelfButton.Visibility = imageFileVisibility;
        _fitImageButton.Visibility = hasRenderedImage ? Visibility.Visible : Visibility.Collapsed;
        UpdateImageFitAction();
    }

    private void ToggleImageFitMode()
    {
        if (_currentImage is null || _currentImageScroll is null)
        {
            return;
        }

        _fitImageToCard = !_fitImageToCard;
        ApplyImageFitMode();
    }

    private void ApplyImageFitMode()
    {
        UpdateImageFitAction();
        if (_currentImage is null || _currentImageScroll is null)
        {
            return;
        }

        if (_fitImageToCard)
        {
            _currentImage.Stretch = Stretch.Uniform;
            _currentImage.StretchDirection = StretchDirection.DownOnly;
            _currentImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _currentImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            UpdateFittedImageBounds();
            _currentImageScroll.ScrollToHorizontalOffset(0);
            _currentImageScroll.ScrollToVerticalOffset(0);
        }
        else
        {
            _currentImage.Width = double.NaN;
            _currentImage.Height = double.NaN;
            _currentImage.Stretch = Stretch.None;
            _currentImage.StretchDirection = StretchDirection.Both;
            _currentImageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _currentImageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
    }

    private void UpdateImageFitAction()
    {
        string label = _fitImageToCard ? "Actual image size" : "Fit image to card";
        SetActionButtonLabel(_fitImageButton, label);
        SetActionButtonHelp(
            _fitImageButton,
            _fitImageToCard
                ? "Show the image at its actual preview size (F)"
                : "Fit the image to the preview card (F)");
        SetActionActive(_fitImageButton, _fitImageToCard);
    }

    private void UpdateFittedImageBounds()
    {
        if (!_fitImageToCard || _currentImage is null || _currentImageScroll is null)
        {
            return;
        }

        Size fitSize = CalculateFittedImageHostSize(
            _currentImageScroll.ViewportWidth,
            _currentImageScroll.ViewportHeight,
            _currentImageScroll.ActualWidth,
            _currentImageScroll.ActualHeight,
            _currentImage.Margin);
        _currentImage.Width = fitSize.Width;
        _currentImage.Height = fitSize.Height;
    }

    internal static Size CalculateFittedImageHostSize(
        double viewportWidth,
        double viewportHeight,
        double fallbackWidth,
        double fallbackHeight,
        Thickness margin)
    {
        double width = UseFinitePositive(viewportWidth) ? viewportWidth : fallbackWidth;
        double height = UseFinitePositive(viewportHeight) ? viewportHeight : fallbackHeight;
        return new Size(
            Math.Max(1, width - margin.Left - margin.Right),
            Math.Max(1, height - margin.Top - margin.Bottom));
    }

    private static bool UseFinitePositive(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

    private void ConfigureCopyContentAction(
        string label,
        string tooltip,
        string? content,
        bool copyCsvFromView = false)
    {
        if (!copyCsvFromView && string.IsNullOrEmpty(content))
        {
            HideCopyContentAction();
            return;
        }

        _currentCopyContent = content;
        _copyCsvFromView = copyCsvFromView;
        SetActionButtonLabel(_copyContentButton, label);
        SetActionButtonHelp(_copyContentButton, tooltip);
        _copyContentButton.Visibility = Visibility.Visible;
    }

    private void CopyCurrentContent()
    {
        string? content = _copyCsvFromView && _csvModel is not null
            ? PreviewClipboardContent.ForCsv(_csvModel, CurrentCsvRows())
            : _currentCopyContent;
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        _actions.CopyContent(content);
    }

    private void CopyFormattedContent()
    {
        if (!string.IsNullOrEmpty(_currentFormattedContent))
        {
            _actions.CopyContent(_currentFormattedContent);
        }
    }

    private IEnumerable<string[]> CurrentCsvRows()
    {
        if (_csvView is IEnumerable view)
        {
            foreach (object? item in view)
            {
                if (item is string[] row)
                {
                    yield return row;
                }
            }

            yield break;
        }

        if (_csvModel is not null)
        {
            foreach (string[] row in _csvModel.Rows)
            {
                yield return row;
            }
        }
    }

    private void ConfirmOpenWithDefaultApp()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        string name = System.IO.Path.GetFileName(_currentFilePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = _currentFilePath;
        }

        _suppressDeactivatedClose = true;
        try
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                $"Open \"{name}\" with the default app?\n\nThis leaves Octadock and may launch another application.\n\n{_currentFilePath}",
                "Open file",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                _actions.OpenWithDefaultApp(_currentFilePath);
            }
        }
        finally
        {
            _suppressDeactivatedClose = false;
        }
    }

    private static void SetActionButtonLabel(Button button, string label)
    {
        button.Tag = label;
        AutomationProperties.SetName(button, label);
    }

    private static void SetActionButtonHelp(Button button, string helpText)
    {
        button.ToolTip = helpText;
        AutomationProperties.SetHelpText(button, helpText);
    }

    private static void SetActionActive(Button button, bool active)
    {
        if (active)
        {
            button.SetResourceReference(Control.BackgroundProperty, ActiveActionResource);
        }
        else
        {
            button.Background = Brushes.Transparent;
        }
    }

    private void InvokePathAction(Action<string> action)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        action(_currentFilePath);
    }

    private void AddCurrentFileToContext()
    {
        // Choosing/creating the first destination opens the Context window. Keep
        // this reviewed preview alive behind it so the user can return and make
        // the deliberate Add action after the destination label updates.
        if (!_actions.ActiveContext.HasActiveContext)
        {
            _contextSelectionCloseGuard.BeginSelection();
        }

        InvokePathAction(_actions.AddToContext);
    }

    private void InvokeModalPathAction(Action<string> action)
    {
        _modalInteractionActive = true;
        _suppressDeactivatedClose = true;
        try
        {
            InvokePathAction(action);
        }
        finally
        {
            // Native file dialogs can send one last Deactivated notification while
            // their HWND is being torn down. Keep the preview alive until that
            // message has drained, then restore the normal click-away behavior.
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _modalInteractionActive = false;
                    _suppressDeactivatedClose = false;
                }),
                DispatcherPriority.ContextIdle);
        }
    }

    internal static PreviewCardPalette CreatePreviewPalette(bool highContrast)
    {
        if (highContrast)
        {
            return new PreviewCardPalette(
                SystemColors.WindowBrush,
                SystemColors.ControlBrush,
                SystemColors.ControlBrush,
                SystemColors.WindowTextBrush,
                SystemColors.WindowTextBrush,
                SystemColors.GrayTextBrush,
                SystemColors.HighlightBrush,
                SystemColors.HighlightBrush,
                SystemColors.ControlBrush,
                SystemColors.WindowTextBrush,
                SystemColors.MenuBrush,
                SystemColors.HighlightBrush,
                SystemColors.HighlightBrush,
                SystemColors.HighlightBrush,
                SystemColors.HighlightBrush,
                SystemColors.WindowTextBrush,
                SystemColors.HighlightBrush,
                SystemColors.WindowTextBrush,
                SystemColors.WindowTextBrush,
                SystemColors.HighlightBrush,
                SystemColors.HighlightBrush);
        }

        return new PreviewCardPalette(
            OctadockDesignTokens.Brushes.PreviewShell,
            OctadockDesignTokens.Brushes.PreviewChrome,
            OctadockDesignTokens.Brushes.PreviewPanel,
            OctadockDesignTokens.Brushes.GlassBorder,
            OctadockDesignTokens.Brushes.Text,
            OctadockDesignTokens.Brushes.TextMuted,
            OctadockDesignTokens.Brushes.Accent,
            OctadockDesignTokens.Brushes.NeutralAccent,
            OctadockDesignTokens.Brushes.Field,
            OctadockDesignTokens.Brushes.Rule,
            OctadockDesignTokens.Brushes.Menu,
            OctadockDesignTokens.Brushes.MenuHover,
            OctadockDesignTokens.Brushes.DangerHover,
            OctadockDesignTokens.Brushes.RowHover,
            OctadockDesignTokens.Brushes.RowSelected,
            OctadockDesignTokens.Brushes.GlassBorderStrong,
            OctadockDesignTokens.Brushes.CaptureHandle,
            OctadockDesignTokens.Brushes.TextSecondaryStrong,
            OctadockDesignTokens.Brushes.GlassHighlight,
            OctadockDesignTokens.Brushes.RowHover,
            OctadockDesignTokens.Brushes.Pressed);
    }

    private static T WithResource<T>(
        T element,
        params (DependencyProperty Property, object ResourceKey)[] references)
        where T : FrameworkElement
    {
        foreach ((DependencyProperty property, object resourceKey) in references)
        {
            element.SetResourceReference(property, resourceKey);
        }

        return element;
    }

    private static Button MakeActionButton(
        string glyph,
        string tooltip,
        Action onClick,
        string menuLabel,
        string? hoverBackgroundResource = null)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                Foreground = TextBrush,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
            },
            Tag = menuLabel,
            ToolTip = tooltip,
            Width = 32,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = true,
        };
        AutomationProperties.SetName(button, menuLabel);
        AutomationProperties.SetHelpText(button, tooltip);
        button.Click += (_, _) => onClick();
        if (button.Content is TextBlock glyphBlock)
        {
            glyphBlock.SetResourceReference(TextBlock.ForegroundProperty, TextResource);
        }

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, OctadockDesignTokens.Radius.Control);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.Name = "Bd";
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new DynamicResourceExtension(hoverBackgroundResource ?? ActionHoverResource),
            "Bd"));
        template.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, SystemColors.HighlightBrush, "Bd"));
        template.Triggers.Add(focus);
        button.Template = template;
        return button;
    }

    private static string AccessibleState(FilePreviewResult result)
        => result.Kind switch
        {
            FilePreviewKind.Loading => $"Loading preview for {System.IO.Path.GetFileName(result.FilePath)}",
            FilePreviewKind.Error => result.Error ?? "Preview failed",
            _ when result.SourceByteLength == 0 => "0 bytes—nothing to preview",
            FilePreviewKind.Csv when result.Scope?.IsSampled == true =>
                $"Preview ready. {result.Scope.Label}",
            _ => $"Preview ready for {System.IO.Path.GetFileName(result.FilePath)}",
        };

    /// <summary>Sorts CSV rows by one column, numerically when the column is a number.</summary>
    private sealed class RowComparer : System.Collections.IComparer
    {
        private readonly int _column;
        private readonly ListSortDirection _direction;
        private readonly bool _numeric;

        public RowComparer(int column, ListSortDirection direction, bool numeric)
        {
            _column = column;
            _direction = direction;
            _numeric = numeric;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Globalization",
            "CA1309:Use ordinal string comparison",
            Justification = "CSV values are presented to users, so text sorting intentionally follows the current culture.")]
        public int Compare(object? x, object? y)
        {
            string a = Cell(x);
            string b = Cell(y);

            int result;
            if (_numeric &&
                double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out double da) &&
                double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out double db))
            {
                result = da.CompareTo(db);
            }
            else
            {
                result = string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
            }

            return _direction == ListSortDirection.Ascending ? result : -result;
        }

        private string Cell(object? item)
            => item is string[] cells && _column < cells.Length ? cells[_column] ?? string.Empty : string.Empty;
    }
}

public sealed record PreviewCardActions(
    Action<string> CopyPath,
    Action<string> CopyContent,
    Action<string> RevealInExplorer,
    Action<string> OpenWithDefaultApp,
    Action<string> SaveCopyAs,
    Action<string> PinImage,
    Action<string> AddToShelf,
    Action<string> Retry,
    Action<string> Locate,
    Action Cancel,
    Action<string> AddToContext,
    ActiveContextState ActiveContext);

internal readonly record struct PreviewCardPalette(
    Brush CardBackground,
    Brush ChromeBackground,
    Brush PanelBackground,
    Brush GlassBorder,
    Brush Text,
    Brush MutedText,
    Brush Accent,
    Brush SecondaryAccent,
    Brush FieldBackground,
    Brush Rule,
    Brush MenuBackground,
    Brush Hover,
    Brush DangerHover,
    Brush RowHover,
    Brush RowSelected,
    Brush ScrollThumb,
    Brush ScrollThumbHover,
    Brush ColumnHeaderText,
    Brush ColumnBorder,
    Brush ColumnHover,
    Brush ColumnPressed);

internal sealed class PreviewContextSelectionCloseGuard
{
    private bool _preserveUntilReturn;

    public void BeginSelection() => _preserveUntilReturn = true;

    public bool ShouldClose(bool transientSuppression, bool uiAudit)
    {
        if (transientSuppression || uiAudit)
        {
            return false;
        }

        if (_preserveUntilReturn)
        {
            return false;
        }

        return true;
    }

    public void OnActivated()
        => _preserveUntilReturn = false;
}

internal sealed record PreviewBadgeInfo(string ShortLabel, string ProviderLabel, string ToolTip);

internal sealed record PreviewInspectorChrome(Visibility Visibility, string ToolTip, string MenuLabel);
