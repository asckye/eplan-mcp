using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace EplanMcp.Spikes.S18.Probe;

/// <summary>
/// probe startup &lt;exe&gt; &lt;out.json&gt; &lt;runs&gt; [app args]   time to the first visible window, Loaded and first frame
/// probe inspect &lt;exe&gt; &lt;out dir&gt; &lt;name&gt; [app args]   UIA tree, DPI, modules, screenshots, tray, single instance
/// probe shot &lt;exe&gt; &lt;out.png&gt; &lt;delay ms&gt; [app args]   one screenshot after the delay
/// probe smoke &lt;exe&gt; &lt;log&gt;                          does it show a window within 20 s? prints the exit code if not
/// Every window the probe starts is closed again (WM_CLOSE or UIA Close; Kill only as a last resort, and reported).
/// </summary>
internal static partial class Program
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static int Main(string[] args)
    {
        NativeMethods.SetProcessDpiAwarenessContext(-4); // per-monitor v2: window rectangles in physical pixels
        return args[0] switch
        {
            "startup" => Startup(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture), args[4..]),
            "inspect" => Inspect(args[1], args[2], args[3], args[4..]),
            "shot" => Shot(args[1], args[2], int.Parse(args[3], CultureInfo.InvariantCulture), args[4..]),
            "smoke" => Smoke(args[1], args[2]),
            _ => 2,
        };
    }

    private static int Startup(string exe, string output, int runs, string[] appArgs)
    {
        var results = new List<Dictionary<string, object?>>();
        string folder = Path.GetDirectoryName(Path.GetFullPath(output))!;
        for (int run = 1; run <= runs; run++)
        {
            string log = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(output)}-run{run}.log");
            File.Delete(log);
            var clock = Stopwatch.StartNew();
            using Process process = Start(exe, log, appArgs);
            (IntPtr window, double visibleMs) = WaitVisible(process, clock);
            WaitLog(log, "first-frame", TimeSpan.FromSeconds(20));
            Dictionary<string, double> events = ReadLog(log).GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First().Ms);
            results.Add(new()
            {
                ["run"] = run,
                ["visibleMs"] = Math.Round(visibleMs, 1),
                ["mainMs"] = events.GetValueOrDefault("main"),
                ["loadedMs"] = events.GetValueOrDefault("loaded"),
                ["firstFrameMs"] = events.GetValueOrDefault("first-frame"),
                ["workingSetMB"] = Math.Round(process.WorkingSet64 / 1048576.0, 1),
                ["closedBy"] = Close(process, window),
            });
            Console.WriteLine(JsonSerializer.Serialize(results[^1]));
            Thread.Sleep(1500);
        }

        double Median(string key) => results.Select(r => Convert.ToDouble(r[key], CultureInfo.InvariantCulture)).Order().ElementAt(results.Count / 2);
        var summary = new Dictionary<string, object?>
        {
            ["exe"] = Path.GetFileName(exe),
            ["args"] = string.Join(' ', appArgs),
            ["runs"] = results,
            ["medianVisibleMs"] = Median("visibleMs"),
            ["medianLoadedMs"] = Median("loadedMs"),
            ["medianFirstFrameMs"] = Median("firstFrameMs"),
        };
        File.WriteAllText(output, JsonSerializer.Serialize(summary, _json));
        return 0;
    }

    private static int Shot(string exe, string png, int delay, string[] appArgs)
    {
        string log = Path.ChangeExtension(png, ".log");
        File.Delete(log);
        using Process process = Start(exe, log, appArgs);
        (IntPtr window, _) = WaitVisible(process, Stopwatch.StartNew());
        WaitLog(log, "first-frame", TimeSpan.FromSeconds(20));
        Thread.Sleep(delay);
        Capture(window, png);
        Console.WriteLine(Close(process, window));
        return 0;
    }

    private static int Smoke(string exe, string log)
    {
        File.Delete(log);
        using Process process = Start(exe, log, []);
        try
        {
            (IntPtr window, double ms) = WaitVisible(process, Stopwatch.StartNew(), TimeSpan.FromSeconds(20));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"window after {ms:F0} ms; {Close(process, window)}"));
            return 0;
        }
        catch (TimeoutException)
        {
            Console.WriteLine(process.HasExited ? $"no window; exit code 0x{process.ExitCode:X8}" : "no window; still running, killed");
            if (!process.HasExited)
            {
                process.Kill();
            }

            return 1;
        }
    }

    private static int Inspect(string exe, string folder, string name, string[] appArgs)
    {
        Directory.CreateDirectory(folder);
        string log = Path.Combine(folder, $"inspect-{name}.log");
        File.Delete(log);
        var report = new Dictionary<string, object?>();
        using Process process = Start(exe, log, appArgs);
        (IntPtr window, _) = WaitVisible(process, Stopwatch.StartNew());
        WaitLog(log, "loaded", TimeSpan.FromSeconds(20));
        if (appArgs.Contains("--font-check"))
        {
            WaitLog(log, "font-check-done", TimeSpan.FromSeconds(60));
        }

        Thread.Sleep(1000);
        report["dpi"] = Dpi(window);
        report["modules"] = Modules(process, Path.GetDirectoryName(Path.GetFullPath(exe))!);

        AutomationElement root = AutomationElement.FromHandle(window);
        AutomationElement Find(string id) => root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            ?? throw new InvalidOperationException("no element " + id);
        report["focusedOnShow"] = Focused(process.Id);
        report["title"] = Describe(Find("Title"));
        report["approve"] = Describe(Find("Approve"));
        report["reject"] = Describe(Find("Reject"));
        report["allButtons"] = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>().Select(b => $"{b.Current.Name} [{b.Current.AutomationId}]").ToList();
        report["countdown"] = Describe(Find("Countdown"));
        Thread.Sleep(2000);
        report["countdownTwoSecondsLater"] = Find("Countdown").Current.Name;
        AutomationElement bar = Find("CountdownBar");
        report["countdownBar"] = Describe(bar);
        report["countdownBarValue"] = bar.TryGetCurrentPattern(RangeValuePattern.Pattern, out object? range)
            ? $"{((RangeValuePattern)range).Current.Value:F1} of {((RangeValuePattern)range).Current.Minimum}..{((RangeValuePattern)range).Current.Maximum}"
            : "(no RangeValue pattern)";
        AutomationElement changesElement = Find("Changes");
        string changes = changesElement.Current.Name.Length > 0 ? changesElement.Current.Name
            : string.Join('\n', changesElement.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>().Select(e => e.Current.Name));
        report["changesName"] = new
        {
            length = changes.Length,
            hiddenCharacters = changes.EnumerateRunes().Count(r => r.Value != '\n' && Rune.GetUnicodeCategory(r) is UnicodeCategory.Control or UnicodeCategory.Format),
            firstLine = changes.Split('\n')[0],
        };
        report["client"] = Find("Client").Current.Name;
        File.WriteAllText(Path.Combine(folder, $"uia-tree-{name}.txt"), Tree(root));

        Capture(window, Path.Combine(folder, $"{name}-light-zh.png"));
        string chineseTitle = Find("Title").Current.Name;
        var switchClock = Stopwatch.StartNew();
        Select(Find("LangEn"));
        while (Find("Title").Current.Name == chineseTitle && switchClock.ElapsedMilliseconds < 5000)
        {
            Thread.Sleep(5);
        }

        report["languageSwitch"] = new { ms = switchClock.ElapsedMilliseconds, from = chineseTitle, to = Find("Title").Current.Name };
        Thread.Sleep(300);
        Capture(window, Path.Combine(folder, $"{name}-light-en.png"));
        Select(Find("ThemeDark"));
        Thread.Sleep(800);
        Capture(window, Path.Combine(folder, $"{name}-dark-en.png"));
        Select(Find("LangZh"));
        Thread.Sleep(400);
        Capture(window, Path.Combine(folder, $"{name}-dark-zh.png"));
        AutomationElement? highContrast = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ThemeHighContrast"));
        if (highContrast is not null)
        {
            Select(highContrast);
            Thread.Sleep(800);
            Capture(window, Path.Combine(folder, $"{name}-hc-zh.png"));
        }

        Select(Find("ThemeLight"));
        Thread.Sleep(400);
        AutomationElement? parameters = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "Parameters"));
        if (parameters is not null)
        {
            report["parametersLink"] = Describe(parameters);
            ((InvokePattern)parameters.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Thread.Sleep(500);
            Capture(window, Path.Combine(folder, $"{name}-light-zh-parameters.png"));
            ((InvokePattern)parameters.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Thread.Sleep(300);
        }

        report["tray"] = Tray(root, window, log, name, Find, process.Id);
        if (name == "A")
        {
            report["singleInstance"] = SingleInstance(exe, folder, log);
        }

        report["closedBy"] = Close(process, window, root);
        report["log"] = ReadLog(log).Where(e => e.Name is not "clean-line").Select(e => $"{e.Ms,8:F1} {e.Name} {e.Detail}").ToList();
        File.WriteAllText(Path.Combine(folder, $"inspect-{name}.json"), JsonSerializer.Serialize(report, _json));
        return 0;
    }

    /// <summary>Minimise, then click the tray icon through the taskbar's own UIA tree; report what happened.</summary>
    private static object Tray(AutomationElement root, IntPtr window, string log, string name, Func<string, AutomationElement> find, int pid)
    {
        ((WindowPattern)root.GetCurrentPattern(WindowPattern.Pattern)).SetWindowVisualState(WindowVisualState.Minimized);
        Thread.Sleep(700);
        int before = ReadLog(log).Count(e => e.Name == "tray-click");
        // Windows 11 puts a new icon into the overflow flyout ("Show hidden icons"); open it if the icon is not on the taskbar.
        AutomationElement? taskbar = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
        IEnumerable<AutomationElement> All(AutomationElement? e) => e is null ? [] : e.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition).Cast<AutomationElement>();
        IEnumerable<AutomationElement> Overflow() => AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition)
            .Cast<AutomationElement>().Where(w => w.Current.ClassName.Contains("Overflow", StringComparison.Ordinal));
        AutomationElement? FindIcon() => All(taskbar).Concat(Overflow().SelectMany(All))
            .FirstOrDefault(e => e.Current.Name.Contains($"S18 原型 {name}）", StringComparison.Ordinal) || e.Current.Name.Contains($"S18 prototype {name})", StringComparison.Ordinal));
        AutomationElement? chevron = All(taskbar).FirstOrDefault(e => e.Current.ClassName == "SystemTray.NormalButton"
            && (e.Current.Name.Contains("隐藏", StringComparison.Ordinal) || e.Current.Name.Contains("hidden", StringComparison.OrdinalIgnoreCase)));
        AutomationElement? icon = FindIcon();
        bool inOverflow = false;
        if (icon is null && chevron is not null)
        {
            ((InvokePattern)chevron.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Thread.Sleep(900);
            icon = FindIcon();
            inOverflow = icon is not null;
        }

        string method = "none";
        if (icon is not null && icon.TryGetCurrentPattern(InvokePattern.Pattern, out object? invoke))
        {
            ((InvokePattern)invoke).Invoke();
            method = inOverflow ? "UIA Invoke on the icon in the overflow flyout" : "UIA Invoke on the taskbar icon";
        }

        bool clicked = WaitFor(() => ReadLog(log).Count(e => e.Name == "tray-click") > before, TimeSpan.FromSeconds(3));
        Thread.Sleep(150);
        object? focusedAfterShow = Focused(pid);
        object? guard = clicked ? Guard(find, log) : null;
        Thread.Sleep(400);
        bool overflowLeftOpen = Overflow().Any(w => !w.Current.IsOffscreen);
        if (overflowLeftOpen && chevron is not null)
        {
            ((InvokePattern)chevron.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); // close the flyout again
        }
        WindowVisualState state = ((WindowPattern)root.GetCurrentPattern(WindowPattern.Pattern)).Current.WindowVisualState;
        return new
        {
            iconFound = icon is not null,
            inOverflow,
            overflowLeftOpen,
            iconName = icon?.Current.Name,
            iconControlType = icon?.Current.LocalizedControlType,
            iconRect = icon?.Current.BoundingRectangle.ToString(CultureInfo.InvariantCulture),
            method,
            clickReachedApp = clicked,
            windowStateAfter = state.ToString(),
            focusedAfterShow,
            focusedAtEnd = Focused(pid),
            foregroundIsWindow = NativeMethods.GetForegroundWindow() == window,
            guard,
        };
    }

    /// <summary>The 500 ms guard: a click right after the window was shown again (tray) is ignored, one 700 ms later is not.</summary>
    private static List<string> Guard(Func<string, AutomationElement> find, string log)
    {
        ((InvokePattern)find("Approve").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Thread.Sleep(700);
        ((InvokePattern)find("Reject").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Thread.Sleep(300);
        return ReadLog(log).Where(e => e.Name is "armed" or "click-ignored" or "answer").TakeLast(4).Select(e => $"{e.Ms:F0} {e.Name} {e.Detail}").ToList();
    }

    private static object SingleInstance(string exe, string folder, string firstLog)
    {
        string log = Path.Combine(folder, "inspect-A-second.log");
        File.Delete(log);
        var clock = Stopwatch.StartNew();
        using Process second = Start(exe, log, []);
        bool exited = second.WaitForExit(15000);
        if (!exited)
        {
            second.Kill();
        }

        Thread.Sleep(500);
        return new
        {
            secondExited = exited,
            secondExitCode = exited ? second.ExitCode : (int?)null,
            secondMs = clock.ElapsedMilliseconds,
            secondLog = ReadLog(log).Select(e => $"{e.Ms:F0} {e.Name} {e.Detail}").ToList(),
            firstSawRedirect = ReadLog(firstLog).Any(e => e.Name == "activated-by-second-instance"),
        };
    }

    private static Process Start(string exe, string log, string[] appArgs)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe))! };
        info.ArgumentList.Add("--log");
        info.ArgumentList.Add(log);
        foreach (string arg in appArgs)
        {
            info.ArgumentList.Add(arg);
        }

        return Process.Start(info)!;
    }

    private static (IntPtr Window, double Ms) WaitVisible(Process process, Stopwatch clock, TimeSpan? timeout = null)
    {
        while (clock.Elapsed < (timeout ?? TimeSpan.FromSeconds(30)) && !process.HasExited)
        {
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == process.Id && NativeMethods.IsWindowVisible(hwnd) && NativeMethods.GetWindowRect(hwnd, out Rect r) && r.Right > r.Left)
                {
                    found = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero)
            {
                return (found, clock.Elapsed.TotalMilliseconds);
            }

            Thread.Sleep(1);
        }

        throw new TimeoutException("no visible window");
    }

    private static string Close(Process process, IntPtr window, AutomationElement? root = null)
    {
        if (root is not null)
        {
            ((WindowPattern)root.GetCurrentPattern(WindowPattern.Pattern)).Close();
        }
        else
        {
            NativeMethods.PostMessageW(window, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        }

        if (process.WaitForExit(10000))
        {
            return root is null ? "WM_CLOSE" : "UIA WindowPattern.Close";
        }

        process.Kill();
        return "Kill (did not exit within 10 s)";
    }

    private static List<(double Ms, string Name, string Detail)> ReadLog(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t'))
            .Where(p => p.Length >= 2)
            .Select(p => (double.Parse(p[0], CultureInfo.InvariantCulture), p[1], p.Length > 2 ? p[2] : "")).ToList();
    }

    private static void WaitLog(string path, string name, TimeSpan timeout)
    {
        if (!WaitFor(() => ReadLog(path).Any(e => e.Name == name), timeout))
        {
            throw new TimeoutException($"no '{name}' in {path}");
        }
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private static void Select(AutomationElement element) =>
        ((SelectionItemPattern)element.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

    /// <summary>
    /// The system focus, described only when it is in the prototype: when another program keeps the foreground
    /// (someone working on the desktop), Windows does not hand it to the new window, and reading another program's
    /// UIA tree (an Electron app) can block.
    /// </summary>
    private static object? Focused(int pid)
    {
        AutomationElement? focused = AutomationElement.FocusedElement;
        if (focused is null)
        {
            return null;
        }

        int owner = focused.Current.ProcessId;
        if (owner == pid)
        {
            return Describe(focused);
        }

        try
        {
            using var other = Process.GetProcessById(owner);
            return new { otherProcess = other.ProcessName };
        }
        catch (ArgumentException)
        {
            return new { otherProcess = owner.ToString(CultureInfo.InvariantCulture) };
        }
    }

    private static object? Describe(AutomationElement? e)
    {
        if (e is null)
        {
            return null;
        }

        AutomationElement.AutomationElementInformation c = e.Current;
        return new
        {
            name = c.Name,
            controlType = c.ControlType.ProgrammaticName,
            localizedControlType = c.LocalizedControlType,
            automationId = c.AutomationId,
            className = c.ClassName,
            frameworkId = c.FrameworkId,
            isKeyboardFocusable = c.IsKeyboardFocusable,
            hasKeyboardFocus = c.HasKeyboardFocus,
            isEnabled = c.IsEnabled,
            isControlElement = c.IsControlElement,
            isContentElement = c.IsContentElement,
            accessKey = c.AccessKey,
            acceleratorKey = c.AcceleratorKey,
            size = $"{c.BoundingRectangle.Width:F0}x{c.BoundingRectangle.Height:F0}",
            patterns = e.GetSupportedPatterns().Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", "", StringComparison.Ordinal)).ToList(),
        };
    }

    private static string Tree(AutomationElement root)
    {
        var text = new StringBuilder();
        void Walk(AutomationElement element, int depth)
        {
            AutomationElement.AutomationElementInformation c = element.Current;
            string name = c.Name.Length > 70 ? c.Name[..70] + "…" : c.Name;
            text.Append(' ', depth * 2).Append(c.ControlType.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal))
                .Append(" \"").Append(name.ReplaceLineEndings("⏎")).Append('"');
            if (c.AutomationId.Length > 0)
            {
                text.Append(" #").Append(c.AutomationId);
            }

            text.Append(c.IsKeyboardFocusable ? " focusable" : "").Append(c.HasKeyboardFocus ? " FOCUSED" : "").AppendLine();
            for (AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(element); child is not null; child = TreeWalker.ControlViewWalker.GetNextSibling(child))
            {
                Walk(child, depth + 1);
            }
        }

        Walk(root, 0);
        return text.ToString();
    }

    private static object Dpi(IntPtr window)
    {
        IntPtr context = NativeMethods.GetWindowDpiAwarenessContext(window);
        IntPtr monitor = NativeMethods.MonitorFromWindow(window, 2);
        NativeMethods.GetDpiForMonitor(monitor, 0, out uint monitorDpi, out _);
        return new
        {
            windowDpi = NativeMethods.GetDpiForWindow(window),
            monitorDpi,
            awareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(context) switch { 0 => "unaware", 1 => "system", 2 => "per-monitor", var v => v.ToString(CultureInfo.InvariantCulture) },
            perMonitorV2 = NativeMethods.AreDpiAwarenessContextsEqual(context, -4),
        };
    }

    /// <summary>Where the UI and runtime DLLs were loaded from: the app folder means the self-contained copy was used.</summary>
    private static object Modules(Process process, string appFolder)
    {
        process.Refresh();
        string[] interesting = ["microsoft.ui.xaml.dll", "microsoft.windowsappruntime.dll", "dwritecore.dll", "coreclr.dll", "presentationcore.dll", "presentationframework.dll", "presentationframework.fluent.dll", "wpfgfx_cor3.dll", "system.windows.forms.dll"];
        List<string> paths = process.Modules.Cast<ProcessModule>().Select(m => m.FileName).ToList();
        return new
        {
            count = paths.Count,
            fromAppFolder = paths.Count(p => p.StartsWith(appFolder, StringComparison.OrdinalIgnoreCase)),
            fromDotnetInstall = paths.Count(p => p.Contains(@"\dotnet\shared\", StringComparison.OrdinalIgnoreCase)),
            fromWindowsAppsPackages = paths.Count(p => p.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)),
            key = paths.Where(p => interesting.Contains(Path.GetFileName(p).ToLowerInvariant()))
                .Select(p => Path.GetFileName(p) + " <- " + (p.StartsWith(appFolder, StringComparison.OrdinalIgnoreCase) ? "app folder" : Path.GetDirectoryName(p))).ToList(),
        };
    }

    /// <summary>Screen copy of the window (made topmost for a moment, not activated), so Mica and shadows are included.</summary>
    private static void Capture(IntPtr window, string path)
    {
        NativeMethods.DwmGetWindowAttribute(window, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out Rect r, Marshal.SizeOf<Rect>());
        NativeMethods.SetWindowPos(window, -1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        Thread.Sleep(400);
        var thread = new Thread(() =>
        {
            int width = r.Right - r.Left;
            int height = r.Bottom - r.Top;
            IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memory = NativeMethods.CreateCompatibleDC(screen);
            IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(screen, width, height);
            IntPtr old = NativeMethods.SelectObject(memory, bitmap);
            NativeMethods.BitBlt(memory, 0, 0, width, height, screen, r.Left, r.Top, 0x00CC0020 | 0x40000000);
            NativeMethods.SelectObject(memory, old);
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (FileStream file = File.Create(path))
            {
                encoder.Save(file);
            }

            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memory);
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        NativeMethods.SetWindowPos(window, -2, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_NOTOPMOST
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static partial class NativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

        [LibraryImport("user32.dll")]
        internal static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetProcessDpiAwarenessContext(nint context);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        internal static partial int GetAwarenessFromDpiAwarenessContext(IntPtr context);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AreDpiAwarenessContextsEqual(IntPtr a, nint b);

        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [LibraryImport("shcore.dll")]
        internal static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        [LibraryImport("dwmapi.dll")]
        internal static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(IntPtr hwnd, nint after, int x, int y, int cx, int cy, uint flags);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetDC(IntPtr hwnd);

        [LibraryImport("user32.dll")]
        internal static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [LibraryImport("gdi32.dll")]
        internal static partial IntPtr CreateCompatibleDC(IntPtr dc);

        [LibraryImport("gdi32.dll")]
        internal static partial IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

        [LibraryImport("gdi32.dll")]
        internal static partial IntPtr SelectObject(IntPtr dc, IntPtr item);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint rop);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteObject(IntPtr item);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteDC(IntPtr dc);
    }
}
