using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace EplanMcp.Large.Tests;

/// <summary>
/// The offline host (M5 design 4.9) on a new scratch copy of the fixture, so that the read tests marked
/// <c>[Trait("Host", "Offline")]</c> run without EPLAN's user interface (README.md, "离线宿主"). <see cref="LiveEplan"/>
/// starts it when <see cref="VariantBinVariable"/> is set and stops it at the end of the run: it exits by itself once no
/// Host has been connected for <see cref="IdleExit"/>, and the stop checks that, a clean exit code and an empty stdout.
/// The copy is deleted after a clean stop and kept otherwise.
/// </summary>
internal sealed class OfflineHost
{
    /// <summary>The Bin of the EPLAN variant to start, such as <c>C:\Program Files\EPLAN\Electric P8\2026.0.3\Bin</c>.</summary>
    public const string VariantBinVariable = "EPLANMCP_OFFLINE_VARIANT_BIN";

    /// <summary>Optional: the offline host's program; by default the build of the tests' own configuration.</summary>
    public const string ExeVariable = "EPLANMCP_OFFLINE_EXE";

    /// <summary>How long the offline host waits without a Host before it exits.</summary>
    public static readonly TimeSpan IdleExit = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan _patience = TimeSpan.FromMinutes(2);

    private readonly Process _process;
    private readonly string _folder;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();

    private OfflineHost(Process process, string folder, string pipePrefix, string projectPath)
    {
        _process = process;
        _folder = folder;
        PipePrefix = pipePrefix;
        ProjectPath = projectPath;
    }

    public int ProcessId => _process.Id;

    /// <summary>The prefix of its pipe, for <c>EPLANMCP_PIPE_PREFIX</c>: no Host of this run finds another EPLAN.</summary>
    public string PipePrefix { get; }

    /// <summary>The copy it opened, without an extension, as EPLAN reports a project path (api-notes A10).</summary>
    public string ProjectPath { get; }

    /// <summary>Copies the fixture, starts the offline host on the copy and waits until its pipe is there.</summary>
    public static async Task<OfflineHost> StartAsync(string variantBin, string scratchRoot, CancellationToken cancellationToken)
    {
        string exe = Environment.GetEnvironmentVariable(ExeVariable) is { Length: > 0 } given ? given : DefaultExe();
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException($"The offline host is not built: {exe}. Build src\\EplanMcp.Bridge.Offline in the configuration of these tests, or set {ExeVariable}.");
        }

        string fixture = Path.Combine(scratchRoot, "fixtures", LiveEplan.ProjectName);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        string folder = Path.Combine(scratchRoot, "offline", stamp);
        string copy = Path.Combine(folder, LiveEplan.ProjectName);
        CopyProject(fixture, copy);

        string prefix = "EplanMcpLarge." + Guid.NewGuid().ToString("N") + ".";
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        string seconds = IdleExit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        foreach (string argument in new[] { "--variant-bin", variantBin, "--project", copy + ".elk", "--pipe-prefix", prefix, "--idle-exit", seconds })
        {
            start.ArgumentList.Add(argument);
        }

        var host = new OfflineHost(Process.Start(start)!, folder, prefix, copy);
        host.Capture();
        await host.WaitForPipeAsync(cancellationToken);
        return host;
    }

    /// <summary>
    /// After the run's Hosts have disconnected: waits for the idle exit, then requires exit code 0 and nothing on
    /// stdout (the offline host never writes there). Ends the process when it does not stop in time.
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            TimeSpan limit = IdleExit + _patience;
            using var timeout = new CancellationTokenSource(limit);
            try
            {
                await _process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _process.Kill();
                throw Failure($"The offline host did not exit within {limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s after the last Host disconnected; it was ended.");
            }

            if (_process.ExitCode != 0)
            {
                throw Failure($"The offline host exited with {_process.ExitCode.ToString(CultureInfo.InvariantCulture)}.");
            }

            string stdout = Read(_stdout);
            if (stdout.Length > 0)
            {
                throw Failure("The offline host wrote to stdout: " + stdout);
            }

            Directory.Delete(_folder, recursive: true);
        }
        finally
        {
            _process.Dispose();
        }
    }

    /// <summary>A plain file copy of the .elk file and the .edb folder, as eng/scripts/eval-fixture.ps1 makes it (api-notes A12).</summary>
    private static void CopyProject(string source, string target)
    {
        if (!File.Exists(source + ".elk"))
        {
            throw new InvalidOperationException($"The fixture {source}.elk is missing: copy it with eng/scripts/eval-fixture.ps1 (without -Open).");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source + ".elk", target + ".elk");
        foreach (string file in Directory.EnumerateFiles(source + ".edb", "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(target + ".edb", Path.GetRelativePath(source + ".edb", file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    /// <summary>The offline host built in this test assembly's configuration, in the same repository.</summary>
    private static string DefaultExe()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory); // tests\EplanMcp.Large.Tests\bin\<configuration>\net10.0
        string configuration = output.Parent!.Name;
        for (DirectoryInfo? directory = output; directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EplanMcp.slnx")))
            {
                return Path.Combine(directory.FullName, "src", "EplanMcp.Bridge.Offline", "bin", configuration, "net481", "EplanMcp.Bridge.Offline.exe");
            }
        }

        throw new InvalidOperationException($"EplanMcp.slnx not found above {AppContext.BaseDirectory}; set {ExeVariable}.");
    }

    private static string Read(StringBuilder text)
    {
        lock (text)
        {
            return text.ToString().Trim();
        }
    }

    private static void Append(StringBuilder text, string? line)
    {
        if (line is not null)
        {
            lock (text)
            {
                text.AppendLine(line);
            }
        }
    }

    private void Capture()
    {
        _process.OutputDataReceived += (_, e) => Append(_stdout, e.Data);
        _process.ErrorDataReceived += (_, e) => Append(_stderr, e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private async Task WaitForPipeAsync(CancellationToken cancellationToken)
    {
        string own = PipePrefix + _process.Id.ToString(CultureInfo.InvariantCulture) + ".";
        var clock = Stopwatch.StartNew();
        while (!Directory.EnumerateFiles(@"\\.\pipe\").Any(pipe => Path.GetFileName(pipe).StartsWith(own, StringComparison.Ordinal)))
        {
            if (_process.HasExited)
            {
                throw Failure($"The offline host exited with {_process.ExitCode.ToString(CultureInfo.InvariantCulture)} before its pipe appeared.");
            }

            if (clock.Elapsed > _patience)
            {
                _process.Kill();
                throw Failure($"The offline host's pipe did not appear within {_patience.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s; it was ended.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private InvalidOperationException Failure(string what) => new(
        $"{what} Its log is bridge-offline-*.log in {Path.Combine(LiveEplan.DataRoot(), "logs")}; the copy is kept in {_folder}. stderr: {Read(_stderr)}");
}
