using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace EplanMcp.Spikes.S18.WpfFluent;

public sealed partial class MainWindow : Window, IDisposable
{
    private static readonly TimeSpan _armDelay = TimeSpan.FromMilliseconds(500);
    private readonly SpikeOptions _options;
    private readonly DateTime _deadline;
    private readonly DispatcherTimer _timer;
    private readonly NotifyIcon _tray;
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
        (options.Theme switch { "light" => ThemeLight, "dark" => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        ApplyTexts();
        _timer.Start();

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
        if (ToolLabel is not null && _tray is not null)
        {
            ApplyTexts();
            SpikeLog.Write("language", UiText.LanguageTag(_language));
        }
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        ThemeMode mode = ReferenceEquals(sender, ThemeLight) ? ThemeMode.Light
            : ReferenceEquals(sender, ThemeDark) ? ThemeMode.Dark
            : ThemeMode.System;
        Application.Current.ThemeMode = mode;
        SpikeLog.Write("theme", mode.Value);
    }

    private void ApplyTexts()
    {
        UiLanguage l = _language;
        Language = XmlLanguage.GetLanguage(UiText.LanguageTag(l));
        Title = UiText.Format("Window.Title", l, "B1 (WPF Fluent)");
        string tip = UiText.Format("Tray.Tip", l, "B1");
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
        LanguageLabel.Text = UiText.Get("Proto.Language", l);
        ThemeLabel.Text = UiText.Get("Proto.Theme", l);
        ThemeSystem.Content = UiText.Get("Proto.ThemeSystem", l);
        ThemeLight.Content = UiText.Get("Proto.ThemeLight", l);
        ThemeDark.Content = UiText.Get("Proto.ThemeDark", l);

        TitleText.Text = UiText.Get("Card.Title", l);
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

        // Neutral bar; the last 20 s use the theme's caution colour (a dynamic reference, so a theme switch follows).
        string barKey = left.TotalSeconds <= 20 ? "SystemFillColorCautionBrush" : "ControlStrongFillColorDefaultBrush";
        CountdownBar.SetResourceReference(ForegroundProperty, barKey);
        if (left.TotalSeconds <= 20)
        {
            CountdownText.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        }
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
        SpikeLog.Write("env", string.Create(CultureInfo.InvariantCulture,
            $"highContrast={SystemParameters.HighContrast} themeMode={Application.Current.ThemeMode.Value} dpi={VisualTreeHelper.GetDpi(this).PixelsPerDip}"));
        foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
        {
            SpikeLog.Write("theme-dictionary", dictionary.Source?.ToString() ?? dictionary.GetType().Name);
        }

        foreach (string key in new[] { "TextFillColorSecondaryBrush", "CardBackgroundFillColorDefaultBrush", "CardStrokeColorDefaultBrush", "SubtleFillColorSecondaryBrush", "ControlStrongFillColorDefaultBrush", "SystemFillColorCautionBrush", "AccentButtonStyle" })
        {
            SpikeLog.Write("resource", key + " = " + (TryFindResource(key)?.GetType().Name ?? "(missing)"));
        }

        SpikeLog.Write("fonts", $"window={FontFamily.Source} title={TitleText.FontFamily.Source} button={ApproveButton.FontFamily.Source} message={SystemFonts.MessageFontFamily.Source}");
        foreach (FontFamilyMap map in new FontFamily("Global User Interface").FamilyMaps.Where(m => Covers(m.Unicode, 0x6279)))
        {
            SpikeLog.Write("composite-font", $"lang={map.Language?.IetfLanguageTag ?? "(any)"} target={map.Target}");
        }
    }

    /// <summary>FontFamilyMap.Unicode is a list such as "0000-052F,1E00-1FFF"; is the code point in it?</summary>
    private static bool Covers(string? ranges, int codePoint) => ranges is null || ranges.Split(',').Any(range =>
    {
        string[] ends = range.Split('-');
        int first = int.Parse(ends[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int last = int.Parse(ends[^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return codePoint >= first && codePoint <= last;
    });

    /// <summary>Same method as prototype A: the candidate whose pixels equal the default rendering is the font used.</summary>
    private void RunFontCheck()
    {
        FontCheckPanel.Visibility = Visibility.Visible;
        string[] candidates = ["Segoe UI Variable", "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimSun", "Yu Gothic UI", "Microsoft JhengHei UI"];
        foreach (string? referenceFamily in new[] { null, "Segoe UI Variable Text" })
        {
        foreach (string tag in new[] { "zh-CN", "en-US" })
        {
            foreach ((string sample, string text) in new[] { ("cjk", "批准修改项目设备标识"), ("latin", "Approve EPLAN change") })
            {
                TextBlock Block(FontFamily? family)
                {
                    var block = new TextBlock { Text = text, FontSize = 14, Language = XmlLanguage.GetLanguage(tag) };
                    if (family is not null)
                    {
                        block.FontFamily = family;
                    }

                    return block;
                }

                var row = new StackPanel();
                TextBlock reference = Block(referenceFamily is null ? null : new FontFamily(referenceFamily));
                row.Children.Add(reference);
                List<(string Name, TextBlock Block)> others = candidates.Select(c => (c, Block(new FontFamily(c)))).ToList();
                others.ForEach(o => row.Children.Add(o.Block));
                FontCheckPanel.Children.Add(row);
                row.UpdateLayout();
                (byte[] expected, byte[] expectedInk) = Render(reference);
                var exact = new List<string>();
                var glyphs = new List<string>();
                foreach ((string name, TextBlock block) in others)
                {
                    (byte[] pixels, byte[] ink) = Render(block);
                    if (pixels.AsSpan().SequenceEqual(expected))
                    {
                        exact.Add(name);
                    }

                    if (ink.AsSpan().SequenceEqual(expectedInk))
                    {
                        glyphs.Add(name);
                    }
                }

                SpikeLog.Write("font-check", $"reference={(referenceFamily is null ? "default" : "explicit")}({reference.FontFamily.Source}) tag={tag} sample={sample} exact=[{string.Join(" | ", exact)}] glyphs=[{string.Join(" | ", glyphs)}]");
                FontCheckPanel.Children.Remove(row);
            }
        }
        }

        FontCheckPanel.Visibility = Visibility.Collapsed;
        SpikeLog.Write("font-check-done");
    }

    private static (byte[] Pixels, byte[] Ink) Render(FrameworkElement element)
    {
        int width = (int)Math.Ceiling(element.ActualWidth);
        int height = (int)Math.Ceiling(element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
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
}
