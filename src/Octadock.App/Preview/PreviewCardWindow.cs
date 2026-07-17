using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.CaptureUx;
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
    private static readonly SolidColorBrush CardBackground = OctadockDesignTokens.Brushes.PreviewShell;
    private static readonly SolidColorBrush ChromeBackground = OctadockDesignTokens.Brushes.PreviewChrome;
    private static readonly SolidColorBrush PanelBackground = OctadockDesignTokens.Brushes.PreviewPanel;
    private static readonly SolidColorBrush GlassBorder = OctadockDesignTokens.Brushes.GlassBorder;
    private static readonly SolidColorBrush TextBrush = OctadockDesignTokens.Brushes.Text;
    private static readonly SolidColorBrush MutedBrush = OctadockDesignTokens.Brushes.TextMuted;
    private static readonly SolidColorBrush AccentBrush = OctadockDesignTokens.Brushes.Accent;
    private static readonly SolidColorBrush WarmAccentBrush = OctadockDesignTokens.Brushes.NeutralAccent;
    private static readonly SolidColorBrush FieldBackground = OctadockDesignTokens.Brushes.Field;
    private static readonly SolidColorBrush HeaderRule = OctadockDesignTokens.Brushes.Rule;
    private static readonly SolidColorBrush MenuBackground = OctadockDesignTokens.Brushes.Menu;
    private static readonly SolidColorBrush MenuHover = OctadockDesignTokens.Brushes.MenuHover;
    private static readonly SolidColorBrush DangerHover = OctadockDesignTokens.Brushes.DangerHover;

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
    private readonly StackPanel _actionHost;
    private readonly Button _copyContentButton;
    private readonly Button _addToShelfButton;
    private readonly Button _fitImageButton;
    private readonly Button _toggleInspectorButton;
    private readonly ContentControl _bodyHost;
    private readonly Border _inspectorHost;

    private string? _currentFilePath;
    private string? _currentCopyContent;
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
    private bool _isClosing;
    private PixelRect? _pendingPhysicalBounds;

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

        _subtitleText = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };

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
        _filterBox.TextChanged += (_, _) => _csvView?.Refresh();

        _filterHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(8, 0, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "Find",
                    Foreground = MutedBrush,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                },
                _filterBox,
            },
        };

        _actionHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
        };

        _copyContentButton = MakeActionButton(CopyGlyph, "Copy preview content", CopyCurrentContent, "Copy content");
        _copyContentButton.Visibility = Visibility.Collapsed;
        _addToShelfButton = MakeActionButton(AddGlyph, "Add this image to the dock", () => InvokePathAction(_actions.AddToShelf), "Add to dock");
        _addToShelfButton.Visibility = Visibility.Collapsed;
        _fitImageButton = MakeActionButton(FitGlyph, "Show image at actual preview size", ToggleImageFitMode, "Actual image size");
        _fitImageButton.Visibility = Visibility.Collapsed;
        _toggleInspectorButton = MakeActionButton(InfoGlyph, "Hide details", ToggleInspector, "Hide details");
        _actionHost.Children.Add(_copyContentButton);
        _actionHost.Children.Add(_addToShelfButton);
        _actionHost.Children.Add(_fitImageButton);
        _actionHost.Children.Add(_toggleInspectorButton);
        _actionHost.Children.Add(MakeActionButton(PathGlyph, "Copy file path", () => InvokePathAction(_actions.CopyPath), "Copy path"));
        _actionHost.Children.Add(MakeActionButton(SaveGlyph, "Save a copy as", () => InvokePathAction(_actions.SaveCopyAs), "Save as..."));
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

        Button closeButton = MakeActionButton(CloseGlyph, "Close (Esc)", RequestClose, "Close preview", DangerHover);
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

        _bodyHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

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
            Effect = new DropShadowEffect
            {
                BlurRadius = 36,
                Direction = 270,
                Opacity = 0.36,
                ShadowDepth = 12,
                Color = Colors.Black,
            },
            Child = layout,
        };
        _cardRoot.ContextMenu = BuildPreviewContextMenu();
        _cardRoot.ContextMenuOpening += (_, _) => _suppressDeactivatedClose = true;
        _cardRoot.ContextMenuClosing += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => _suppressDeactivatedClose = false), DispatcherPriority.ContextIdle);
        Content = _cardRoot;

        Deactivated += (_, _) =>
        {
            if (!_suppressDeactivatedClose)
            {
                RequestClose();
            }
        }; // click-away dismiss
        PreviewKeyDown += OnPreviewKeyDown;
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
        _inspectorHost.Child = BuildInspector(result);
        UpdateInspectorChrome();

        SizeToOwningMonitor();
        if (!IsVisible)
        {
            Show();
        }

        ApplyPendingPhysicalBounds();
        Activate();
        PlayOpenAnimation();
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
        HideCopyContentAction();
        _currentImage = null;
        _currentImageScroll = null;
        _csvView = null;
        _csvModel = null;
        _sortColumn = -1;
        ConfigureImageActions(result);

        switch (result.Kind)
        {
            case FilePreviewKind.Csv when result.Csv is not null:
                _filterBox.Text = string.Empty;
                _filterHost.Visibility = Visibility.Visible;
                object csvBody = BuildCsvBody(result.Csv);
                ConfigureCopyContentAction(
                    "Copy table",
                    "Copy the visible preview rows as tab-separated text",
                    content: null,
                    copyCsvFromView: true);
                return csvBody;

            case FilePreviewKind.PlainText:
                _filterHost.Visibility = Visibility.Collapsed;
                ConfigureCopyContentAction("Copy text", "Copy the preview text", result.Text);
                return BuildTextBody(result.Text ?? string.Empty);

            case FilePreviewKind.Markdown:
                _filterHost.Visibility = Visibility.Collapsed;
                ConfigureCopyContentAction("Copy text", "Copy the markdown source", result.Text);
                return BuildMarkdownBody(result.Text ?? string.Empty);

            case FilePreviewKind.Image when result.ImagePath is not null:
                _filterHost.Visibility = Visibility.Collapsed;
                return BuildImageBody(result);

            case FilePreviewKind.FileInfo:
                _filterHost.Visibility = Visibility.Collapsed;
                ConfigureCopyContentAction("Copy info", "Copy the file information", result.Text);
                return BuildFileInfoBody(result.Text ?? string.Empty);

            default:
                _filterHost.Visibility = Visibility.Collapsed;
                return BuildErrorBody(result.Error ?? "Preview failed.");
        }
    }

    private UIElement BuildInspector(FilePreviewResult result)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(14, 13, 14, 14),
        };

        panel.Children.Add(MakeInspectorHeader(result));
        panel.Children.Add(new TextBlock
        {
            Text = "DETAILS",
            Foreground = AccentBrush,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

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

        var badgeFrame = new Border
        {
            MinWidth = 42,
            Height = 34,
            Padding = new Thickness(7, 0, 7, 0),
            Background = OctadockDesignTokens.Brushes.ActiveAction,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Control,
            Child = new TextBlock
            {
                Text = badge.ShortLabel,
                Foreground = TextBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = badge.ProviderLabel,
                    Foreground = TextBrush,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                new TextBlock
                {
                    Text = "Preview details",
                    Foreground = MutedBrush,
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0),
                },
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
                new TextBlock
                {
                    Text = row.Label.ToUpperInvariant(),
                    Foreground = MutedBrush,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 3),
                },
                new TextBlock
                {
                    Text = row.Value,
                    Foreground = TextBrush,
                    FontSize = 12,
                    TextWrapping = row.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    TextTrimming = row.Wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                },
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
            Foreground = MutedBrush,
            HorizontalContentAlignment = rightAlign ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };

        var cellFactory = new FrameworkElementFactory(typeof(TextBlock));
        cellFactory.SetBinding(TextBlock.TextProperty, new Binding($"[{index}]"));
        cellFactory.SetValue(TextBlock.ForegroundProperty, TextBrush);
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
        footer.Children.Add(StatLabel(model.Columns[col].Name, AccentBrush));
        if (count == 0)
        {
            footer.Children.Add(StatLabel("no numeric values", MutedBrush));
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

    private static Border WrapFooter(UIElement content) => new()
    {
        BorderBrush = HeaderRule,
        BorderThickness = new Thickness(0, 1, 0, 0),
        Child = content,
    };

    private static TextBlock StatLabel(string text, Brush brush) => new()
    {
        Text = text,
        Foreground = brush,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 14, 0),
    };

    private static UIElement Stat(string label, string value)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = label + " ",
            Foreground = MutedBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = TextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private static Border WrapBodyFrame(UIElement content)
    {
        var frame = new Grid();
        frame.Children.Add(content);
        frame.Children.Add(new Border
        {
            Height = 2,
            Background = AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0.72,
            IsHitTestVisible = false,
        });
        frame.Children.Add(new Border
        {
            Width = 2,
            Background = WarmAccentBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0.42,
            IsHitTestVisible = false,
        });

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = HeaderRule,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Panel,
            Margin = new Thickness(14, 12, 14, 14),
            ClipToBounds = true,
            Child = frame,
        };
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

        return WrapBodyFrame(editor);
    }

    // ---- Markdown body -------------------------------------------------------

    /// <summary>Rendered markdown (headings/lists/code/quotes); links show their URL as a tooltip only.</summary>
    private UIElement BuildMarkdownBody(string markdown)
    {
        var palette = new MarkdownPalette(
            TextBrush,
            MutedBrush,
            AccentBrush,
            FieldBackground,
            HeaderRule);

        var viewer = new FlowDocumentScrollViewer
        {
            Document = MarkdownRendering.BuildDocument(markdown, palette),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsToolBarVisible = false,
            ContextMenu = BuildPreviewContextMenu(),
        };

        return WrapBodyFrame(viewer);
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

        panel.Children.Add(new TextBlock
        {
            Text = "Preview unavailable",
            Foreground = TextBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = MutedBrush,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        });

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = HeaderRule,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Panel,
            Margin = new Thickness(22),
            Child = panel,
        };
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
        catch (Exception ex)
        {
            _currentImage = null;
            _currentImageScroll = null;
            _fitImageButton.Visibility = Visibility.Collapsed;
            return BuildErrorBody($"The image could not be displayed.\n{ex.Message}");
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

        panel.Children.Add(new TextBlock
        {
            Text = "There's no built-in preview for this file type.",
            Foreground = TextBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "You can still show it in File Explorer, copy its path, or open it with the default app.",
            Foreground = MutedBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var rows = new StackPanel();
        foreach ((string label, string value) in ParseFileInfoRows(details))
        {
            rows.Children.Add(MakeInfoRow(label, value));
        }

        panel.Children.Add(rows);

        return new Border
        {
            Background = PanelBackground,
            BorderBrush = HeaderRule,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Panel,
            Margin = new Thickness(20),
            Child = panel,
            ContextMenu = BuildPreviewContextMenu(),
        };
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

        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = TextBrush,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            TextWrapping = TextWrapping.Wrap,
        };

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
                new Border
                {
                    Height = 2,
                    Background = WarmAccentBrush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(8, 0, 8, 7),
                    Opacity = 0.9,
                },
                labelStack,
            },
        };

        return new Border
        {
            Width = 72,
            Height = 44,
            Margin = new Thickness(0, 0, 14, 0),
            Background = PanelBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = OctadockDesignTokens.Radius.Rail,
            Child = frame,
        };
    }

    private static Style CreateListViewItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, OctadockDesignTokens.Brushes.RowHover));
        style.Triggers.Add(hover);

        var selected = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, OctadockDesignTokens.Brushes.RowSelected));
        style.Triggers.Add(selected);
        return style;
    }

    private static ResourceDictionary CreatePreviewResources()
        => (ResourceDictionary)XamlReader.Parse(
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
                <Setter Property="Foreground" Value="#77FFFFFF"/>
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
                          <Setter Property="Foreground" Value="#B8FFFFFF"/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>

              <Style TargetType="{x:Type GridViewColumnHeader}">
                <Setter Property="Foreground" Value="#B0CBD5E1"/>
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
                              BorderBrush="#20FFFFFF"
                              BorderThickness="0,0,1,1"
                              Padding="{TemplateBinding Padding}"
                              TextElement.Foreground="{TemplateBinding Foreground}">
                        <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                          RecognizesAccessKey="True"/>
                      </Border>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                          <Setter TargetName="Root" Property="Background" Value="#18FFFFFF"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                          <Setter TargetName="Root" Property="Background" Value="#28FFFFFF"/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
            </ResourceDictionary>
            """);

    private ContextMenu BuildPreviewContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = MenuBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            HasDropShadow = true,
            SnapsToDevicePixels = true,
        };
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

        menu.Items.Add(MakeMenuItem("Copy path", () => InvokePathAction(_actions.CopyPath)));
        menu.Items.Add(MakeMenuItem("Save a copy as...", () => InvokePathAction(_actions.SaveCopyAs)));
        menu.Items.Add(MakeMenuItem("Show in File Explorer", () => InvokePathAction(_actions.RevealInExplorer)));
        menu.Items.Add(MakeMenuItem("Open externally...", ConfirmOpenWithDefaultApp));
        menu.Items.Add(MakeMenuSeparator());
        menu.Items.Add(MakeMenuItem("Close preview", RequestClose));
    }

    private static Style CreateMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
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
        hover.Setters.Add(new Setter(Border.BackgroundProperty, MenuHover, "Bd"));
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

    private static Border MakeMenuSeparator() => new()
    {
        Height = 1,
        Background = HeaderRule,
        Margin = new Thickness(8, 5, 8, 5),
    };

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
        _toggleInspectorButton.Background = _inspectorVisible
            ? OctadockDesignTokens.Brushes.ActiveAction
            : Brushes.Transparent;
        _toggleInspectorButton.ToolTip = chrome.ToolTip;
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

    private void ConfigureImageActions(FilePreviewResult result)
    {
        bool hasRenderedImage = result.Kind == FilePreviewKind.Image &&
            !string.IsNullOrWhiteSpace(result.ImagePath);
        bool isImageFile = hasRenderedImage || ImageFileSupport.IsSupportedRasterPath(result.FilePath);
        Visibility imageFileVisibility = isImageFile
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
        _fitImageButton.ToolTip = _fitImageToCard
            ? "Show the image at its actual preview size (F)"
            : "Fit the image to the preview card (F)";
        _fitImageButton.Background = _fitImageToCard
            ? OctadockDesignTokens.Brushes.ActiveAction
            : Brushes.Transparent;
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
        _copyContentButton.ToolTip = tooltip;
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
    }

    private void InvokePathAction(Action<string> action)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        action(_currentFilePath);
    }

    private static Button MakeActionButton(
        string glyph,
        string tooltip,
        Action onClick,
        string menuLabel,
        Brush? hoverBackground = null)
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
            Focusable = false,
        };
        button.Click += (_, _) => onClick();

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, OctadockDesignTokens.Radius.Control);
        border.Name = "Bd";
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            hoverBackground ?? OctadockDesignTokens.Brushes.ActionHover,
            "Bd"));
        template.Triggers.Add(hover);
        button.Template = template;
        return button;
    }

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

internal sealed record PreviewCardActions(
    Action<string> CopyPath,
    Action<string> CopyContent,
    Action<string> RevealInExplorer,
    Action<string> OpenWithDefaultApp,
    Action<string> SaveCopyAs,
    Action<string> PinImage,
    Action<string> AddToShelf);

internal sealed record PreviewBadgeInfo(string ShortLabel, string ProviderLabel, string ToolTip);

internal sealed record PreviewInspectorChrome(Visibility Visibility, string ToolTip, string MenuLabel);
