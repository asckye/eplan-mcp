using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Themes;
using TiaOpenness.Gui.ViewModels;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace EplanMcp.Spikes.S18.Wpf;

/// <summary>One operation row of the card: action, object (project text, cleaned), tool.</summary>
internal sealed record OperationRow(string Action, string Object, string Tool);

public sealed partial class MainWindow : Window, IDisposable
{
    private static readonly TimeSpan _armDelay = TimeSpan.FromMilliseconds(500);
    private static readonly int[] _announceAt = [30, 10];
    private readonly SpikeOptions _options;
    private readonly DateTime _deadline;
    private readonly DispatcherTimer _timer;
    private readonly NotifyIcon _tray;
    private readonly HashSet<int> _announced = [];
    private UiLanguage _language;
    private long _armedAt;
    private bool _answered;
    private bool _firstActivation = true;
    private bool _focusTitleOnActivation;

    internal MainWindow(SpikeOptions options)
    {
        _options = options;
        InitializeComponent();
        if (options.FontCheck)
        {
            Height = 1040;
        }

        _deadline = DateTime.UtcNow.AddSeconds(options.DeadlineSeconds);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => UpdateCountdown(), Dispatcher);
        _tray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                ShowFromOutside("tray-click");
            }
        };
        SpikeLog.Write("tray-added");

        _language = options.Language;
        (options.Language == UiLanguage.English ? LangEn : LangZh).IsChecked = true;
        (options.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, "hc" => ThemeHighContrast, _ => ThemeSystem }).IsChecked = true;
        ApplyTexts();
        _timer.Start();

        ThemeManager.Current.PropertyChanged += OnThemeChanged;
        SourceInitialized += (_, _) => ApplyTitleBar();
        Loaded += OnLoaded;
        ContentRendered += (_, _) =>
        {
            SpikeLog.Write("first-frame");
            FocusTitle("rendered");
        };
        Activated += (_, _) =>
        {
            if (_firstActivation || _focusTitleOnActivation)
            {
                SpikeLog.Write("activated", _firstActivation ? "first" : "after-show");
                bool again = _focusTitleOnActivation;
                _firstActivation = _focusTitleOnActivation = false;
                if (again)
                {
                    FocusTitle("activated");
                    _ = Dispatcher.BeginInvoke(() => FocusTitle("activated-deferred"), DispatcherPriority.Input);
                }
            }
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Arm("visible");
            }
        };
        Closed += (_, _) =>
        {
            Dispose();
            SpikeLog.Write("closed");
        };
        SpikeLog.Write("window-created");
    }

    public void Dispose()
    {
        _timer.Stop();
        ThemeManager.Current.PropertyChanged -= OnThemeChanged;
        _tray.Visible = false;
        _tray.Dispose();
    }

    private void ShowFromOutside(string reason)
    {
        SpikeLog.Write(reason);
        _focusTitleOnActivation = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Arm(reason);
        FocusTitle(reason);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SpikeLog.Write("loaded");
        Arm("loaded");
        FocusTitle("loaded");
        DumpEnvironment();
        if (_options.FontCheck)
        {
            _ = Dispatcher.BeginInvoke(RunFontCheck, DispatcherPriority.ApplicationIdle);
        }
    }

    private void FocusTitle(string reason)
    {
        IInputElement? focused = Keyboard.Focus(TitleText);
        SpikeLog.Write("focus-title", string.Create(CultureInfo.InvariantCulture, $"{reason} result={ReferenceEquals(focused, TitleText)}"));
    }

    private void Arm(string reason)
    {
        _armedAt = Stopwatch.GetTimestamp();
        SpikeLog.Write("armed", reason);
    }

    private void OnLanguageChecked(object sender, RoutedEventArgs e)
    {
        _language = ReferenceEquals(sender, LangEn) ? UiLanguage.English : UiLanguage.Chinese;
        if (_tray is not null)
        {
            ApplyTexts();
            SpikeLog.Write("language", UiText.LanguageTag(_language));
        }
    }

    /// <summary>TIA's ThemeManager swaps the palette dictionary; high contrast here is its override, not the system mode.</summary>
    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ThemeManager theme = ThemeManager.Current;
        theme.HighContrastOverride = ReferenceEquals(sender, ThemeHighContrast) ? true : null;
        theme.Theme = ReferenceEquals(sender, ThemeLight) ? AppTheme.Light : ReferenceEquals(sender, ThemeDark) ? AppTheme.Dark : AppTheme.Auto;
        SpikeLog.Write("theme", string.Create(CultureInfo.InvariantCulture, $"theme={theme.Theme} dark={theme.EffectivelyDark} highContrast={theme.EffectivelyHighContrast}"));
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeManager.EffectivelyDark))
        {
            ApplyTitleBar();
        }
    }

    /// <summary>The standard title bar follows the palette (TIA draws its own chrome instead).</summary>
    private void ApplyTitleBar()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int dark = ThemeManager.Current.EffectivelyDark && !ThemeManager.Current.EffectivelyHighContrast ? 1 : 0;
            _ = NativeMethods.DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        }
    }

    private void ApplyTexts()
    {
        UiLanguage l = _language;
        Language = XmlLanguage.GetLanguage(UiText.LanguageTag(l));
        Title = UiText.Format("Window.Title", l, "B (WPF + TIA)");
        string tip = UiText.Format("Tray.Tip", l, "B");
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
        PageTitle.Text = UiText.Format("Window.Title", l, "B (WPF + TIA)");
        LanguageLabel.Text = UiText.Get("Proto.Language", l);
        ThemeLabel.Text = UiText.Get("Proto.Theme", l);
        ThemeSystem.Content = UiText.Get("Proto.ThemeSystem", l);
        ThemeLight.Content = UiText.Get("Proto.ThemeLight", l);
        ThemeDark.Content = UiText.Get("Proto.ThemeDark", l);
        ThemeHighContrast.Content = UiText.Get("Proto.ThemeHighContrast", l);

        TitleText.Text = UiText.Get("Card.Title", l);
        StateText.Text = UiText.Get("Card.State", l);
        ClientValue.Text = SampleRequest.Client;
        UnverifiedText.Text = UiText.Get("Card.UnverifiedTag", l);
        AutomationProperties.SetName(ClientValue, UiText.Format("Card.Unverified", l, SampleRequest.Client));
        TransportText.Text = SampleRequest.Transport;
        HostText.Text = UiText.Format("Card.Host", l, SampleRequest.HostVersion, SampleRequest.HostPid);
        ProjectLabel.Text = UiText.Get("Card.Project", l);
        ProjectValue.Text = SampleRequest.Project;
        InstanceLabel.Text = UiText.Get("Card.Instance", l);
        InstanceValue.Text = SampleRequest.Instance;

        CleanChangeList changes = ChangeListText.Clean(SampleRequest.Changes);
        var rows = changes.Lines.Select(line => new OperationRow(UiText.Get("Op.Update", l), line, SampleRequest.Tool)).ToList();
        if (changes.MoreLines > 0)
        {
            rows.Add(new OperationRow("", UiText.Format("Card.MoreLines", l, changes.MoreLines), ""));
        }

        Operations.ItemsSource = rows;
        ChangesSource.Text = UiText.Get("Card.ChangesSource", l) + " · " + UiText.Format("Card.Removed", l, changes.Removed);
        Risks.ItemsSource = new[] { UiText.Get("Card.RiskWrite", l), UiText.Get("Card.RiskReal", l) };
        MetadataText.Text = SampleRequest.RequestId;
        AutoRejectText.Text = UiText.Get("Card.AutoReject", l);
        AutomationProperties.SetName(CountdownBar, UiText.Get("Card.CountdownName", l));
        ParametersButton.Content = UiText.Get("Card.Parameters", l);
        RejectButton.Content = UiText.Get("Card.Reject", l);
        ApproveButton.Content = UiText.Get("Card.Approve", l);
        ParametersText.Text = ParametersJson();
        UpdateCountdown();
    }

    /// <summary>
    /// The call's arguments as JSON for the code block. Every control and format character that is still literal
    /// after serialisation (U+202E, zero-width and tag characters) is written as a \u escape, so it cannot reorder or
    /// hide text; letters stay readable.
    /// </summary>
    private static string ParametersJson()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        string json = JsonSerializer.Serialize(new { tool = SampleRequest.Tool, project = SampleRequest.Project, changes = SampleRequest.Changes }, options);
        var visible = new System.Text.StringBuilder(json.Length);
        foreach (char c in json)
        {
            if (char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate && c != '\n' && c != '\r')
            {
                visible.Append(string.Create(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}"));
            }
            else
            {
                visible.Append(c);
            }
        }

        return visible.ToString();
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
        CountdownText.Text = clock;

        // The clock alone ("1:58") means nothing to a screen reader; the accessible name says what it counts down to.
        AutomationProperties.SetName(CountdownText, UiText.Format("Card.Countdown", _language, clock));
        CountdownBar.Value = left.TotalSeconds * 100 / _options.DeadlineSeconds;

        // Primer's tone triggers: the last 20 s in the theme's warning colour, on the text and the bar.
        FeatureTone tone = left.TotalSeconds <= 20 ? FeatureTone.Warning : FeatureTone.Normal;
        CountdownText.Tag = tone;
        if (tone == FeatureTone.Warning)
        {
            CountdownBar.SetResourceReference(ForegroundProperty, "Primer.warn");
        }

        // APR-06: announce only at fixed thresholds, never every second.
        foreach (int threshold in _announceAt)
        {
            if (left.TotalSeconds <= threshold && _announced.Add(threshold))
            {
                AccessibleStatus.SetText(CountdownText, UiText.Format("Card.Announce", _language, threshold));
                SpikeLog.Write("announce", threshold.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private void OnParameters(object sender, RoutedEventArgs e) =>
        ParametersBlock.Visibility = ParametersBlock.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

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
        ThemeManager theme = ThemeManager.Current;
        SpikeLog.Write("env", string.Create(CultureInfo.InvariantCulture,
            $"systemHighContrast={SystemParameters.HighContrast} theme={theme.Theme} dark={theme.EffectivelyDark} highContrast={theme.EffectivelyHighContrast} dpi={VisualTreeHelper.GetDpi(this).PixelsPerDip}"));
        foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
        {
            SpikeLog.Write("theme-dictionary", dictionary.Source?.ToString() ?? dictionary.GetType().Name);
        }

        SpikeLog.Write("fonts", $"window={FontFamily.Source} title={TitleText.FontFamily.Source} button={ApproveButton.FontFamily.Source} countdown={CountdownText.FontFamily.Source}");
        SpikeLog.Write("buttons", string.Create(CultureInfo.InvariantCulture,
            $"approve={ApproveButton.ActualWidth}x{ApproveButton.ActualHeight} reject={RejectButton.ActualWidth}x{RejectButton.ActualHeight} isDefault={ApproveButton.IsDefault}/{RejectButton.IsDefault}"));
    }

    /// <summary>
    /// Which font the text really uses (same pixel comparison as prototype A), and which FontFamily syntaxes reach
    /// the files in the app folder: the folder as base URI, a file URI, siteoforigin, the bare family name.
    /// </summary>
    private void RunFontCheck()
    {
        FontCheckPanel.Visibility = Visibility.Visible;
        string? family = ((App)Application.Current).BundledFamily;
        var candidates = new List<(string Name, FontFamily Family)>
        {
            ("Segoe UI", new FontFamily("Segoe UI")),
            ("Microsoft YaHei UI", new FontFamily("Microsoft YaHei UI")),
            ("Consolas", new FontFamily("Consolas")),
        };
        if (family is not null)
        {
            string folder = App.FontFolder.AbsoluteUri;
            candidates.Add(("folder ./#family", new FontFamily(App.FontFolder, "./#" + family)));
            candidates.Add(("folder ./Regular.ttf#family", new FontFamily(App.FontFolder, "./MapleMono-CN-Regular.ttf#" + family)));
            candidates.Add(("file:///…/Fonts/#family", new FontFamily(folder + "#" + family)));
            candidates.Add(("pack://siteoforigin:,,,/Fonts/#family", new FontFamily("pack://siteoforigin:,,,/Fonts/#" + family)));
            candidates.Add(("name only", new FontFamily(family)));
        }

        foreach (string tag in new[] { "zh-CN", "en-US" })
        {
            foreach ((string sample, string text) in new[] { ("cjk", "批准修改项目设备标识"), ("latin", "Approve EPLAN change") })
            {
                List<string> matches = Match(text, tag, FontWeights.Normal, (FontFamily)FindResource("Ui.Font"), candidates);
                SpikeLog.Write("font-check", $"reference=Ui.Font tag={tag} sample={sample} matches=[{string.Join(" | ", matches)}]");
            }
        }

        if (family is not null)
        {
            var weights = new List<(string Name, FontFamily Family)>
            {
                ("SemiBold file", new FontFamily(App.FontFolder, "./MapleMono-CN-SemiBold.ttf#" + family)),
                ("Regular file (simulated bold)", new FontFamily(App.FontFolder, "./MapleMono-CN-Regular.ttf#" + family)),
            };
            List<string> matches = Match("Approve EPLAN 批准修改", "zh-CN", FontWeights.SemiBold, (FontFamily)FindResource("Ui.Font"), weights);
            SpikeLog.Write("font-weight", $"semibold text with Ui.Font matches=[{string.Join(" | ", matches)}]");
        }

        FontCheckPanel.Visibility = Visibility.Collapsed;
        SpikeLog.Write("font-check-done");
    }

    private List<string> Match(string text, string tag, FontWeight weight, FontFamily reference, IEnumerable<(string Name, FontFamily Family)> candidates)
    {
        TextBlock Block(FontFamily family) => new() { Text = text, FontSize = 14, FontWeight = weight, FontFamily = family, Language = XmlLanguage.GetLanguage(tag) };
        var row = new StackPanel();
        TextBlock first = Block(reference);
        row.Children.Add(first);
        List<(string Name, TextBlock Block)> others = candidates.Select(c => (c.Name, Block(c.Family))).ToList();
        others.ForEach(o => row.Children.Add(o.Block));
        FontCheckPanel.Children.Add(row);
        row.UpdateLayout();
        byte[] expected = Render(first);
        List<string> matches = others.Where(o => Render(o.Block).AsSpan().SequenceEqual(expected)).Select(o => o.Name).ToList();
        FontCheckPanel.Children.Remove(row);
        return matches;
    }

    private static byte[] Render(FrameworkElement element)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
