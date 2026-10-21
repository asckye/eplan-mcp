using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace EplanMcp.Spikes.S18.WinUi;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan _armDelay = TimeSpan.FromMilliseconds(500);
    private readonly SpikeOptions _options;
    private readonly DateTime _deadline;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private readonly TrayIcon _tray;
    private UiLanguage _language;
    private long _armedAt;
    private bool _answered;
    private bool _firstActivation = true;
    private bool _focusTitleOnActivation;
    private bool _firstFrame = true;

    internal MainWindow(SpikeOptions options)
    {
        _options = options;
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(820 * scale), (int)((options.FontCheck ? 1040 : 920) * scale)));

        _deadline = DateTime.UtcNow.AddSeconds(options.DeadlineSeconds);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => UpdateCountdown();
        _tray = new TrayIcon("S18", ShowFromOutside);

        _language = options.Language;
        (options.Language == UiLanguage.English ? LangEn : LangZh).IsChecked = true;
        (options.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        ApplyTexts();
        UpdateCountdown();
        _timer.Start();

        Root.Loaded += OnRootLoaded;
        Activated += OnActivated;
        VisibilityChanged += (_, e) =>
        {
            if (e.Visible)
            {
                Arm("visible");
            }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _tray.Dispose();
            SpikeLog.Write("closed");
        };
        SpikeLog.Write("window-created");
    }

    /// <summary>Tray click or a redirected second instance: show, restore, activate, re-arm, focus the title.</summary>
    internal void ShowFromOutside(string reason)
    {
        SpikeLog.Write(reason);
        _focusTitleOnActivation = true;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        Arm(reason);
        FocusTitle(reason);
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        SpikeLog.Write("loaded");
        CompositionTarget.Rendering += OnFirstRendering;
        Arm("loaded");
        FocusTitle("loaded");
        DumpEnvironment();
        if (_options.FontCheck)
        {
            _ = RunFontCheckAsync();
        }
    }

    private void OnFirstRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstRendering;
        if (_firstFrame)
        {
            _firstFrame = false;
            SpikeLog.Write("first-frame");
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        // Activation restores the last focused control; when the window was shown from the tray or by a second
        // instance, the title must win, so focus it again once the activation has arrived.
        if (_firstActivation || _focusTitleOnActivation)
        {
            SpikeLog.Write("activated", _firstActivation ? "first" : "after-show");
            _firstActivation = _focusTitleOnActivation = false;
            FocusTitle("activated");
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusTitle("activated-deferred"));
        }
    }

    private void FocusTitle(string reason)
    {
        bool focused = TitleHost.Focus(FocusState.Programmatic);
        SpikeLog.Write("focus-title", string.Create(CultureInfo.InvariantCulture, $"{reason} result={focused}"));
    }

    private void Arm(string reason)
    {
        _armedAt = Stopwatch.GetTimestamp();
        SpikeLog.Write("armed", reason);
    }

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        _language = ReferenceEquals(sender, LangEn) ? UiLanguage.English : UiLanguage.Chinese;
        if (Root is not null && ToolLabel is not null)
        {
            ApplyTexts();
            SpikeLog.Write("language", UiText.LanguageTag(_language));
        }
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        (ElementTheme theme, TitleBarTheme bar) = ReferenceEquals(sender, ThemeLight) ? (ElementTheme.Light, TitleBarTheme.Light)
            : ReferenceEquals(sender, ThemeDark) ? (ElementTheme.Dark, TitleBarTheme.Dark)
            : (ElementTheme.Default, TitleBarTheme.UseDefaultAppMode);
        Root.RequestedTheme = theme;
        AppWindow.TitleBar.PreferredTheme = bar;
        SpikeLog.Write("theme", string.Create(CultureInfo.InvariantCulture, $"requested={theme} actual={Root.ActualTheme}"));
    }

    private void ApplyTexts()
    {
        UiLanguage l = _language;
        Root.Language = UiText.LanguageTag(l);
        Title = UiText.Format("Window.Title", l, "A (WinUI 3)");
        _tray.SetTip(UiText.Format("Tray.Tip", l, "A"));
        LanguageLabel.Text = UiText.Get("Proto.Language", l);
        ThemeLabel.Text = UiText.Get("Proto.Theme", l);
        ThemeSystem.Content = UiText.Get("Proto.ThemeSystem", l);
        ThemeLight.Content = UiText.Get("Proto.ThemeLight", l);
        ThemeDark.Content = UiText.Get("Proto.ThemeDark", l);

        string title = UiText.Get("Card.Title", l);
        TitleText.Text = title;
        AutomationProperties.SetName(TitleHost, title);
        ToolLabel.Text = UiText.Get("Card.Tool", l);
        ToolValue.Text = SampleRequest.Tool;
        TierLabel.Text = UiText.Get("Card.Tier", l);
        TierValue.Text = SampleRequest.Tier;
        ProjectLabel.Text = UiText.Get("Card.Project", l);
        ProjectValue.Text = SampleRequest.Project;
        InstanceLabel.Text = UiText.Get("Card.Instance", l);
        InstanceValue.Text = SampleRequest.Instance;
        ClientLabel.Text = UiText.Get("Card.Client", l);
        ClientValue.Text = UiText.Format("Card.Unverified", l, SampleRequest.Client);

        CleanChangeList changes = ChangeListText.Clean(SampleRequest.Changes);
        var text = new List<string>(changes.Lines);
        if (changes.MoreLines > 0)
        {
            text.Add(UiText.Format("Card.MoreLines", l, changes.MoreLines));
        }

        ChangesLabel.Text = UiText.Get("Card.Changes", l);
        ChangesSource.Text = UiText.Get("Card.ChangesSource", l) + " · " + UiText.Format("Card.Removed", l, changes.Removed);
        ChangesText.Text = string.Join('\n', text);
        UndoText.Text = UiText.Format("Card.Undo", l, SampleRequest.Tool);
        ApproveButton.Content = UiText.Get("Card.Approve", l);
        RejectButton.Content = UiText.Get("Card.Reject", l);
        AutomationProperties.SetName(CountdownBar, UiText.Get("Card.CountdownName", l));
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        TimeSpan left = _deadline - DateTime.UtcNow;
        if (left <= TimeSpan.Zero || _answered)
        {
            if (!_answered)
            {
                _answered = true;
                CountdownText.Text = UiText.Get("Card.Expired", _language);
                ApproveButton.IsEnabled = RejectButton.IsEnabled = false;
                _timer.Stop();
            }

            CountdownBar.Value = 0;
            return;
        }

        string clock = string.Create(CultureInfo.InvariantCulture, $"{(int)left.TotalMinutes}:{left.Seconds:00}");
        CountdownText.Text = UiText.Format("Card.Countdown", _language, clock);
        CountdownBar.Value = left.TotalSeconds * 100 / _options.DeadlineSeconds;
        bool warning = left.TotalSeconds <= 20;
        CountdownWarning.Visibility = warning ? Visibility.Visible : Visibility.Collapsed;
        CountdownBar.ShowPaused = warning;
    }

    private void OnAnswer(object sender, RoutedEventArgs e)
    {
        TimeSpan sinceArmed = Stopwatch.GetElapsedTime(_armedAt);
        if (sinceArmed < _armDelay)
        {
            SpikeLog.Write("click-ignored", string.Create(CultureInfo.InvariantCulture, $"{sinceArmed.TotalMilliseconds:F0} ms after arming"));
            return;
        }

        bool approved = ReferenceEquals(sender, ApproveButton);
        ResultText.Text = UiText.Get(approved ? "Card.Approved" : "Card.Rejected", _language);
        _answered = true;
        ApproveButton.IsEnabled = RejectButton.IsEnabled = false;
        _timer.Stop();
        SpikeLog.Write("answer", approved ? "approve" : "reject");
    }

    private void DumpEnvironment()
    {
        var accessibility = new Windows.UI.ViewManagement.AccessibilitySettings();
        var ui = new Windows.UI.ViewManagement.UISettings();
        Windows.UI.Color background = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        ResourceDictionary controls = Application.Current.Resources.MergedDictionaries[0];
        SpikeLog.Write("env", string.Create(CultureInfo.InvariantCulture,
            $"highContrast={accessibility.HighContrast} scheme={accessibility.HighContrastScheme} systemBackground=#{background.R:X2}{background.G:X2}{background.B:X2} appTheme={Application.Current.RequestedTheme} actual={Root.ActualTheme} scale={Root.XamlRoot.RasterizationScale}"));
        SpikeLog.Write("theme-dictionaries", string.Create(CultureInfo.InvariantCulture,
            $"controls={controls.GetType().Name}[{string.Join(',', controls.ThemeDictionaries.Keys)}] app=[{string.Join(',', Application.Current.Resources.ThemeDictionaries.Keys)}]"));
        if (controls.ThemeDictionaries.TryGetValue("HighContrast", out object? hc) && hc is ResourceDictionary hcDictionary)
        {
            foreach (string key in new[] { "ButtonBackground", "ButtonForeground", "CardBackgroundFillColorDefaultBrush", "ProgressBarForeground", "SystemFillColorCautionBrush" })
            {
                SpikeLog.Write("hc-resource", key + " = " + Describe(hcDictionary.TryGetValue(key, out object? v) ? v : null));
            }
        }

        SpikeLog.Write("fonts", $"title={TitleText.FontFamily.Source} button={ApproveButton.FontFamily.Source} changes={ChangesText.FontFamily.Source}");
        foreach (string line in ChangeListText.Clean(SampleRequest.Changes).Lines)
        {
            SpikeLog.Write("clean-line", ChangeListText.Describe(line));
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "(missing)",
        SolidColorBrush b => string.Create(CultureInfo.InvariantCulture, $"SolidColorBrush #{b.Color.A:X2}{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"),
        _ => value.GetType().Name,
    };

    /// <summary>
    /// Renders the same text with a reference font family and with candidates, and compares the pixels. "exact" means
    /// the same pixels in the same place (same glyphs and line metrics); "glyphs" compares only the inked area, so a
    /// candidate with the same outlines but other line metrics (Microsoft YaHei and YaHei UI) also matches. Done for
    /// WinUI's default (XamlAutoFontFamily) and for the Workbench family with zh-CN and en-US tags; with files in
    /// Fonts\ also for several FontFamily syntaxes and for each extra weight file.
    /// </summary>
    private async Task RunFontCheckAsync()
    {
        FontCheckPanel.Visibility = Visibility.Visible;
        var candidates = new List<string> { "Segoe UI Variable", "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimSun", "Yu Gothic UI", "Microsoft JhengHei UI" };
        BundledFontFile? bundled = App.Instance?.Font;
        string? regularUri = null;
        if (bundled is not null)
        {
            string file = System.IO.Path.GetFileName(bundled.Path);
            regularUri = $"ms-appx:///Fonts/{file}#{bundled.Family}";
            candidates.AddRange([regularUri, $"{new Uri(bundled.Path).AbsoluteUri}#{bundled.Family}", $"/Fonts/{file}#{bundled.Family}",
                $"ms-appx:///Fonts/#{bundled.Family}", bundled.Family]);
        }

        var workbench = (FontFamily)Application.Current.Resources["WorkbenchFontFamily"];
        var strong = (FontFamily)Application.Current.Resources["WorkbenchStrongFontFamily"];
        foreach ((string name, FontFamily reference) in new[] { ("default", new FontFamily("XamlAutoFontFamily")), ("workbench", workbench) })
        {
            foreach (string tag in new[] { "zh-CN", "en-US" })
            {
                foreach ((string sample, string text) in new[] { ("cjk", "批准修改项目设备标识"), ("latin", "Approve EPLAN change") })
                {
                    (List<string> exact, List<string> glyphs) = await MatchAsync(reference, text, tag, FontWeights.Normal, candidates);
                    SpikeLog.Write("font-check", $"reference={name} tag={tag} sample={sample} exact=[{string.Join(" | ", exact)}] glyphs=[{string.Join(" | ", glyphs)}]");
                }
            }
        }

        if (bundled is not null && regularUri is not null)
        {
            foreach (string file in Directory.EnumerateFiles(BundledFont.Folder, "*.ttf").Where(f => f != bundled.Path))
            {
                string weightName = System.IO.Path.GetFileNameWithoutExtension(file).Split('-')[^1];
                Windows.UI.Text.FontWeight weight = weightName switch
                {
                    "Thin" => FontWeights.Thin,
                    "ExtraLight" => FontWeights.ExtraLight,
                    "Light" => FontWeights.Light,
                    "Medium" => FontWeights.Medium,
                    "SemiBold" => FontWeights.SemiBold,
                    "Bold" => FontWeights.Bold,
                    "ExtraBold" => FontWeights.ExtraBold,
                    _ => FontWeights.Normal,
                };
                string fileUri = $"ms-appx:///Fonts/{System.IO.Path.GetFileName(file)}#{bundled.Family}";
                (List<string> exact, _) = await MatchAsync(workbench, "Approve EPLAN 批准修改", "zh-CN", weight, [fileUri, regularUri]);
                (List<string> strongExact, _) = await MatchAsync(strong, "Approve EPLAN 批准修改", "zh-CN", weight, [fileUri, regularUri]);
                SpikeLog.Write("font-weight", $"file={System.IO.Path.GetFileName(file)} weight={weight.Weight} workbench-exact=[{string.Join(" | ", exact)}] strong-exact=[{string.Join(" | ", strongExact)}]");
            }
        }

        FontCheckPanel.Visibility = Visibility.Collapsed;
        SpikeLog.Write("font-check-done");
    }

    private async Task<(List<string> Exact, List<string> Glyphs)> MatchAsync(FontFamily reference, string text, string tag, Windows.UI.Text.FontWeight weight, IEnumerable<string> candidates)
    {
        TextBlock Block(FontFamily family) => new() { Text = text, FontSize = 14, Language = tag, FontWeight = weight, FontFamily = family };
        var row = new StackPanel();
        TextBlock first = Block(reference);
        row.Children.Add(first);
        List<(string Name, TextBlock Block)> others = candidates.Select(c => (c, Block(new FontFamily(c)))).ToList();
        others.ForEach(o => row.Children.Add(o.Block));
        FontCheckPanel.Children.Add(row);
        await Task.Delay(300);
        (byte[] expected, byte[] expectedInk) = await RenderAsync(first);
        var exact = new List<string>();
        var glyphs = new List<string>();
        foreach ((string name, TextBlock block) in others)
        {
            (byte[] pixels, byte[] ink) = await RenderAsync(block);
            if (pixels.AsSpan().SequenceEqual(expected))
            {
                exact.Add(name);
            }

            if (ink.AsSpan().SequenceEqual(expectedInk))
            {
                glyphs.Add(name);
            }
        }

        FontCheckPanel.Children.Remove(row);
        return (exact, glyphs);
    }

    /// <summary>All pixels (BGRA), and the inked area alone (cropped to non-transparent pixels, with its size).</summary>
    private static async Task<(byte[] Pixels, byte[] Ink)> RenderAsync(UIElement element)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[(((y * width) + x) * 4) + 3] != 0)
                {
                    (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                }
            }
        }

        if (right < 0)
        {
            return (pixels, []);
        }

        var ink = new List<byte>(BitConverter.GetBytes(right - left));
        for (int y = top; y <= bottom; y++)
        {
            ink.AddRange(pixels.AsSpan(((y * width) + left) * 4, (right - left + 1) * 4).ToArray());
        }

        return (pixels, ink.ToArray());
    }
    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(IntPtr window);
    }
}
