using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.CaptureUx;
using Octadock.App.Clipboard;
using Octadock.App.DependencyInjection;
using Octadock.App.Editing;
using Octadock.App.History;
using Octadock.App.TextTools;
using Octadock.App.Theming;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.DependencyInjection;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;
using Octadock.Data.DependencyInjection;
using Octadock.Platform.Windows.DependencyInjection;
using Octadock.Platform.Windows.Monitors;

// Opt-in local acceptance probe. Uses an isolated data root and synthetic content.
// Does not register protocol handlers, start clipboard monitoring, or touch user captures.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        DpiAwareness.EnsurePerMonitorV2();
        if (args.Contains("--fixture")) return RunFixture(output);
        int result = 1;

        var app = new Application();

        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await RunProbe(output); result = 0; Console.WriteLine("Desktop probe passed. Evidence: " + output); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.Error.WriteLine(ex); }
            finally
            {
                foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        Dispatcher.Run();
        return result;
    }

    private static int RunFixture(string output)
    {
        var fixture = new Window
        {
            Title = "Octadock capture test fixture", Width = 540, Height = 380,
            ShowActivated = false, Background = Brushes.Teal,
            Content = new TextBlock { Text = "LOCAL CAPTURE FIXTURE\nWindow capture regression", FontSize = 28, Foreground = Brushes.White, Margin = new Thickness(30) },
        };
        fixture.Show();
        File.WriteAllText(Path.Combine(output, "fixture-hwnd.txt"), new WindowInteropHelper(fixture).Handle.ToInt64().ToString());
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(35) };
        timer.Tick += (_, _) => { fixture.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); };
        timer.Start();
        Dispatcher.Run();
        return 0;
    }

    private static async Task RunProbe(string output)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOctadockCore(Path.Combine(output, "profile", Guid.NewGuid().ToString("N")));
        services.AddOctadockData(); services.AddOctadockPlatformWindows(); services.AddOctadockApp();
        services.AddCaptureUx(); services.AddEditing();
        services.AddSingleton<ShelfViewModel>(); services.AddSingleton<ShelfWindow>(); services.AddClipboardHistory(); services.AddTextTools();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Octadock.App.App.SetServiceProvider(provider);
        await provider.GetRequiredService<IOctadockDatabase>().InitializeAsync();
        var settings = provider.GetRequiredService<ISettingsService>();
        await settings.LoadAsync();
        await settings.UpdateAsync(s => s with { General = s.General with { Theme = ThemePreference.Dark } });
        provider.GetRequiredService<ThemeManager>().Initialize();
        var monitors = provider.GetRequiredService<IMonitorService>();
        var results = new List<object>();
        results.Add(new { Kind = "connected-displays", Displays = monitors.GetMonitors() });

        // Create real image files and records so History and the shelf bind real thumbnails.
        var paths = provider.GetRequiredService<IStoragePaths>();
        Directory.CreateDirectory(paths.CapturesDirectory);
        var records = new List<CaptureRecord>();
        for (int i = 0; i < 6; i++)
        {
            string relative = $"Captures/probe-{i}.png";
            var sample = new Border { Width = 480, Height = 260, Background = i % 2 == 0 ? Brushes.DarkSlateBlue : Brushes.Teal,
                Child = new TextBlock { Text = $"Capture {i + 1}\nSynthetic local preview", Foreground = Brushes.White, FontSize = 30, Margin = new Thickness(24) } };
            sample.Measure(new Size(480, 260)); sample.Arrange(new Rect(0, 0, 480, 260));
            SaveVisual(sample, Path.Combine(paths.RootDirectory, relative));
            var record = new CaptureRecord { Id = Guid.NewGuid(), Type = CaptureType.Area, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                OriginalPath = relative, ThumbnailPath = relative, PixelWidth = 480, PixelHeight = 260,
                Source = new CaptureSource("Fixture", $"Example {i + 1}", null) };
            records.Add(record);
            await provider.GetRequiredService<ICaptureRepository>().AddAsync(record);
        }
        foreach (Type type in new[] { typeof(Octadock.App.FirstRun.FirstRunWindow), typeof(Octadock.App.About.AboutWindow), typeof(HistoryWindow), typeof(Octadock.App.Settings.SettingsWindow),
            typeof(Octadock.App.Context.ContextWindow), typeof(Octadock.App.Ai.AgentWorkspaceWindow) })
        {
            var window = (Window)ActivatorUtilities.CreateInstance(provider, type);
            window.ShowActivated = false;
            window.Show();
            await Task.Delay(350);
            if (window.DataContext is Octadock.App.Ai.AgentWorkspaceViewModel export)
            {
                export.SelectedRecentCapture = export.RecentCaptures.First();
                await export.AddRecentCaptureCommand.ExecuteAsync(null);
                export.ChooseWorkflowCommand.Execute(export.Workflows.Last());
                await Task.Delay(250);
                if (!export.HasReview) throw new InvalidOperationException("Local export did not build its review.");
            }
            foreach (var targetMonitor in monitors.GetMonitors())
            {
            NativeMethods.PositionPhysical(new WindowInteropHelper(window).Handle, new PixelRect(targetMonitor.WorkArea.X + 24, targetMonitor.WorkArea.Y + 24, 640, 500));
            await Task.Delay(250);
            foreach (double requestedWidth in new[] { 1100d, 620d })
            {
                window.MinWidth = 0; window.MinHeight = 0;
                window.Width = Math.Min(requestedWidth, window.MaxWidth); window.Height = Math.Min(650, window.MaxHeight);
                if (window.DataContext is HistoryViewModel history) history.SelectedItem = history.Items.FirstOrDefault();
                await Task.Delay(200);
                window.UpdateLayout();
                SaveVisual(window, Path.Combine(output, $"{type.Name}-display{targetMonitor.Index}-{requestedWidth}.png"));
                var rect = GetBounds(window); var monitor = monitors.GetMonitorFromPoint(rect.Center);
                if (rect.Left < monitor.WorkArea.Left || rect.Right > monitor.WorkArea.Right || rect.Top < monitor.WorkArea.Top || rect.Bottom > monitor.WorkArea.Bottom)
                    throw new InvalidOperationException($"{type.Name} escaped its display: {rect}");
                results.Add(new { Kind = "window", Name = type.Name, Display = targetMonitor.DeviceName, RequestedWidth = requestedWidth, Bounds = rect });
            }
            }
            window.Close();
        }
        var shelfVm = provider.GetRequiredService<ShelfViewModel>();
        foreach (var record in records) shelfVm.Add(record);
        var shelf = provider.GetRequiredService<ShelfWindow>();
        shelf.Show(); await Task.Delay(300);
        SaveVisual((FrameworkElement)shelf.Content, Path.Combine(output, "Shelf.png"));
        var picker = provider.GetRequiredService<IWindowPicker>();
        if (picker.EnumerateWindows().Any(w => w.Handle.Value == new WindowInteropHelper(shelf).Handle.ToInt64()))
            throw new InvalidOperationException("The picker included its own UI.");
        shelf.Close();
        var dock = new DockPill();
        foreach (var display in monitors.GetMonitors())
        {
            dock.ShowOn(display); await Task.Delay(180);
            var rect = GetBounds(dock);
            if (rect.Left < display.WorkArea.Left || rect.Right > display.WorkArea.Right || rect.Top < display.WorkArea.Top || rect.Bottom > display.WorkArea.Bottom)
                throw new InvalidOperationException("Dock escaped display: " + display.DeviceName);
            results.Add(new { Kind = "dock-placement", display.DeviceName, Bounds = rect });
        }
        dock.Close();

        // An independent process verifies WGC/GDI window capture of actual rendered pixels.
        string fixturePath = Path.Combine(output, "fixture-hwnd.txt");
        if (File.Exists(fixturePath)) File.Delete(fixturePath);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add(output); start.ArgumentList.Add("--fixture");
        using var fixtureProcess = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start.");
        try
        {
            for (int i = 0; i < 50 && !File.Exists(fixturePath); i++) await Task.Delay(100);
            long handle = long.Parse(await File.ReadAllTextAsync(fixturePath));
            await Task.Delay(300);
            var found = picker.EnumerateWindows().Single(w => w.Handle.Value == handle);
            var frame = await provider.GetRequiredService<ICaptureEngine>().CaptureWindowAsync(new WindowCaptureRequest { Window = new WindowHandle(handle) });
            if (frame.Width < 200 || frame.Height < 100 || frame.Pixels.Span.ToArray().Distinct().Count() < 8)
                throw new InvalidOperationException("Window capture returned blank or invalid pixels.");
            results.Add(new { Kind = "native-window-capture", frame.Width, frame.Height, found.Title });
        }
        finally { fixtureProcess.CloseMainWindow(); }
        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

    }

    private static PixelRect GetBounds(Window window)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle, out NativeRect r);
        return new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
    private static void SaveVisual(FrameworkElement visual, string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(visual.ActualWidth)), Math.Max(1, (int)Math.Ceiling(visual.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
}
