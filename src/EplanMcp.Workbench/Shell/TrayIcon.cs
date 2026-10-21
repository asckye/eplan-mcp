using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench.Shell;

/// <summary>
/// The tray icon (workbench-ui STS-09, NAV-09), WinForms' NotifyIcon as in TIA (ADR-0017 decision 1). The icon carries
/// the number of waiting questions as a badge and its tip says it in words; the menu opens the window or exits. It
/// names a count only, never project text (T20). Its colours are the palette's tokens (COL-01).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>The longest tip Windows shows.</summary>
    private const int MaxTip = 127;

    /// <summary>The longest tool name a notification names; EPLAN's tool names are far shorter.</summary>
    private const int MaxToolName = 64;

    private readonly NotifyIcon _icon = new();
    private readonly ToolStripMenuItem _open;
    private readonly ToolStripMenuItem _exit;
    private int _pending;
    private Action? _openReminded;

    public TrayIcon(Action open, Action exit)
    {
        _open = new ToolStripMenuItem(string.Empty, null, (_, _) => open());
        _exit = new ToolStripMenuItem(string.Empty, null, (_, _) => exit());
        _icon.ContextMenuStrip = new ContextMenuStrip();
        _icon.ContextMenuStrip.Items.AddRange([_open, _exit]);
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                open();
            }
        };
        _icon.BalloonTipClicked += (_, _) => _openReminded?.Invoke();
        _icon.BalloonTipClosed += (_, _) => _openReminded = null;
        Loc.Current.LanguageChanged += OnLanguageChanged;
        ThemeManager.Current.PropertyChanged += OnThemeChanged;
        Update();
        _icon.Visible = true;
    }

    /// <summary>How many questions wait; the badge and the tip follow it.</summary>
    public int Pending
    {
        get => _pending;
        set
        {
            _pending = value;
            Update();
        }
    }

    /// <summary>The tip: the product, or the count in words (STS-10).</summary>
    public static string Tip(int pending)
    {
        string tip = pending == 0 ? Loc.Current["App.Name"] : Loc.Current.T("Tray.Pending", pending);
        return tip.Length > MaxTip ? tip[..MaxTip] : tip;
    }

    /// <summary>
    /// The notification for a new question while the window is hidden or minimized (STS-09). It may show on the lock
    /// screen, so it says the count and the tool and nothing from the project (T20); clicking it opens that question.
    /// </summary>
    public void Remind(int pending, string tool, Action open)
    {
        _openReminded = open;
        _icon.ShowBalloonTip(0, Loc.Current["App.Name"], ReminderText(pending, tool), ToolTipIcon.None);
    }

    /// <summary>"3 questions wait for approval · new: eplan_page_delete"; a tool name that is not one is left out.</summary>
    public static string ReminderText(int pending, string tool)
    {
        string count = Loc.Current.T("Tray.Pending", pending);
        return IsToolName(tool) ? Loc.Current.T("Tray.Arrived", count, tool) : count;
    }

    /// <summary>Tool names are lowercase identifiers; anything else could carry text from a project (T20).</summary>
    internal static bool IsToolName(string tool) =>
        tool.Length is > 0 and <= MaxToolName && tool.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    /// <summary>The badge on the icon: nothing, the count, or "9+".</summary>
    public static string Badge(int pending) => pending switch
    {
        <= 0 => string.Empty,
        < 10 => pending.ToString(CultureInfo.CurrentCulture),
        _ => "9+",
    };

    public void Dispose()
    {
        Loc.Current.LanguageChanged -= OnLanguageChanged;
        ThemeManager.Current.PropertyChanged -= OnThemeChanged;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Update();

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        _open.Text = Loc.Current["Tray.Open"];
        _exit.Text = Loc.Current["Tray.Exit"];
        _icon.Text = Tip(_pending);
        Icon? previous = _icon.Icon;
        _icon.Icon = Draw(Badge(_pending));
        previous?.Dispose();
    }

    /// <summary>The brand dot of the Sider, with the badge on its lower right when questions wait.</summary>
    private static Icon Draw(string badge)
    {
        System.Drawing.Size size = SystemInformation.SmallIconSize;
        using var bitmap = new Bitmap(size.Width, size.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brand = new SolidBrush(Token("Ant.colorPrimary"));
            graphics.FillEllipse(brand, 1, 1, size.Width - 2, size.Height - 2);
            if (badge.Length > 0)
            {
                var area = new RectangleF(size.Width * 0.35f, size.Height * 0.35f, size.Width * 0.65f, size.Height * 0.65f);
                using var fill = new SolidBrush(Token("Primer.warn"));
                using var ink = new SolidBrush(Token("Primer.onAccent"));
                using var font = new Font(SystemFonts.MessageBoxFont!.FontFamily, size.Height * 0.42f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
                using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                graphics.FillEllipse(fill, area);
                graphics.DrawString(badge, font, ink, area, centred);
            }
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static System.Drawing.Color Token(string key)
    {
        System.Windows.Media.Color color = ((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.FindResource(key)).Color;
        return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
