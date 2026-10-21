using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using EplanMcp.Spikes.S19.Probe;

// Spike S19 driver (see ../README.md). Every server it starts listens on 127.0.0.1 only and gets a key generated here,
// passed in its environment and never printed.
//   S19.Probe startup <label> <exe> <http|stdio|stdio-arg> <runs>   start-up times: 1 first run + <runs> measured runs
//   S19.Probe checks <exe> <Stateless|Stateful|StatefulForInitializeClients|default> <logdir> [--gate]
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return args switch
{
    ["startup", string label, string exe, string mode, string runs] => await StartupAsync(label, exe, mode, int.Parse(runs, CultureInfo.InvariantCulture)),
    ["checks", string exe, string mode, string logs, .. string[] rest] => await ChecksAsync(exe, mode, logs, rest.Contains("--gate")),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: S19.Probe startup <label> <exe> <http|stdio|stdio-arg> <runs> | checks <exe> <mode> <logdir> [--gate]");
    return 2;
}

static async Task<int> StartupAsync(string label, string exe, string mode, int runs)
{
    var ready = new List<double>();
    var init = new List<double>();
    var memory = new List<double>();
    for (int run = 0; run <= runs; run++)
    {
        string key = Server.NewKey();
        var environment = new Dictionary<string, string?> { ["S19_KEY"] = key, ["S19_PORT"] = "0" };
        string[] arguments = mode == "stdio-arg" ? ["stdio"] : [];
        var watch = Stopwatch.StartNew();
        await using Server server = Server.Start(exe, arguments, environment);
        double readyMs, initMs;
        if (mode == "http")
        {
            Uri endpoint = await server.WaitReadyAsync(TimeSpan.FromSeconds(30));
            readyMs = watch.Elapsed.TotalMilliseconds;
            using var mcp = new Mcp(endpoint, key);
            Reply reply = await mcp.PostAsync(Mcp.Initialize(1));
            initMs = watch.Elapsed.TotalMilliseconds;
            Check(reply.Status == 200, "initialize returned " + reply.Status);
        }
        else
        {
            string line = await server.WriteAndReadLineAsync(Mcp.Initialize(1), TimeSpan.FromSeconds(30));
            initMs = watch.Elapsed.TotalMilliseconds;
            readyMs = double.NaN;
            Check(line.Contains("\"protocolVersion\"", StringComparison.Ordinal), "stdio initialize answered: " + line);
        }

        server.Process.Refresh();
        double privateMb = server.Process.PrivateMemorySize64 / 1048576.0;
        Console.WriteLine($"{label}\t{(run == 0 ? "first" : "run " + run)}\tready {readyMs:F0} ms\tinitialize {initMs:F0} ms\tprivate {privateMb:F1} MB");
        if (run > 0)
        {
            ready.Add(readyMs);
            init.Add(initMs);
            memory.Add(privateMb);
        }
    }

    Console.WriteLine($"{label}\tMEDIAN of {runs}\tready {Median(ready):F0} ms (min {ready.Min():F0}, max {ready.Max():F0})\tinitialize {Median(init):F0} ms (min {init.Min():F0}, max {init.Max():F0})\tprivate {Median(memory):F1} MB");
    return 0;
}

static async Task<int> ChecksAsync(string exe, string mode, string logs, bool gate)
{
    Directory.CreateDirectory(logs);
    var rows = new Rows();
    await SessionChecksAsync(exe, mode, logs, rows);
    if (gate)
    {
        await GateChecksAsync(exe, logs, rows);
        await LockoutChecksAsync(exe, logs, rows);
    }

    Console.WriteLine(rows.Render());
    return rows.Failed ? 1 : 0;
}

static async Task SessionChecksAsync(string exe, string mode, string logs, Rows rows)
{
    string key = Server.NewKey();
    var environment = new Dictionary<string, string?>
    {
        ["S19_KEY"] = key,
        ["S19_PORT"] = "0",
        ["S19_SESSION_MODE"] = mode == "default" ? null : mode,
        ["S19_REQUEST_LOG"] = Path.Combine(logs, $"requests-session-{mode}.jsonl"),
    };
    await using Server server = Server.Start(exe, [], environment);
    Uri endpoint = await server.WaitReadyAsync(TimeSpan.FromSeconds(30));
    rows.Note($"[{mode}] {server.Stderr.FirstOrDefault(l => l.StartsWith("S19 listening", StringComparison.Ordinal))}");
    using var mcp = new Mcp(endpoint, key);
    string p = Mcp.Protocol2025;

    Reply a = await mcp.PostAsync(Mcp.Initialize(1, "probe-A"));
    string? sessionA = a.SessionId;
    rows.Info("S1", $"[{mode}] initialize (2025-11-25) with the key", $"{a.Status}, Mcp-Session-Id {(sessionA is null ? "absent" : "present")}, protocolVersion {a.Json?["result"]?["protocolVersion"]}, {a.ContentType}");
    Reply n = await mcp.PostAsync(Mcp.Notification("notifications/initialized"), sessionA, p);
    rows.Info("S2", $"[{mode}] notifications/initialized", n.Status.ToString(CultureInfo.InvariantCulture));
    Reply list = await mcp.PostAsync(mcp.Request("tools/list"), sessionA, p);
    rows.Info("S3", $"[{mode}] tools/list in session A", $"{list.Status}: {string.Join(", ", (list.Json?["result"]?["tools"]?.AsArray() ?? []).Select(t => t?["name"]?.ToString()))}{ErrorOf(list)}");
    Reply who = await mcp.PostAsync(mcp.Call("s19_whoami"), sessionA, p);
    rows.Info("S4", $"[{mode}] s19_whoami in session A", $"{who.Status}: {who.Text}{ErrorOf(who)}");

    Reply b = await mcp.PostAsync(Mcp.Initialize(2, "probe-B"));
    string? sessionB = b.SessionId;
    await mcp.PostAsync(Mcp.Notification("notifications/initialized"), sessionB, p);
    rows.Info("S5", $"[{mode}] second initialize (session B)", $"{b.Status}, {(sessionB is null ? "no session ID" : sessionB == sessionA ? "SAME session ID" : "different session ID")}");

    await mcp.PostAsync(mcp.Call("s19_select", new JsonObject { ["instance"] = 1 }), sessionA, p);
    await mcp.PostAsync(mcp.Call("s19_select", new JsonObject { ["instance"] = 2 }), sessionB, p);
    Reply selA = await mcp.PostAsync(mcp.Call("s19_selected"), sessionA, p);
    Reply selB = await mcp.PostAsync(mcp.Call("s19_selected"), sessionB, p);
    rows.Info("S6", $"[{mode}] A selects 1, B selects 2; then each reads its selection", $"A: {selA.Text ?? selA.Status.ToString(CultureInfo.InvariantCulture)}; B: {selB.Text ?? selB.Status.ToString(CultureInfo.InvariantCulture)}");

    Reply noKey = await mcp.PostAsync(mcp.Request("tools/list"), sessionA, p, authorization: string.Empty);
    rows.Expect("S7", $"[{mode}] session A's ID without the key", 401, noKey.Status);
    Reply unknown = await mcp.PostAsync(mcp.Request("tools/list"), "0000000000000000", p);
    rows.Info("S8", $"[{mode}] unknown Mcp-Session-Id with the key", $"{unknown.Status}{ErrorOf(unknown)}");
    Reply none = await mcp.PostAsync(mcp.Request("tools/list"), null, p);
    rows.Info("S9", $"[{mode}] tools/list without Mcp-Session-Id or initialize (2025-11-25)", $"{none.Status}{ErrorOf(none)}");

    Reply list26 = await mcp.PostAsync(mcp.Request2026("tools/list"), null, Mcp.Protocol2026);
    rows.Info("S10", $"[{mode}] tools/list as 2026-07-28 (no initialize, _meta client info)", $"{list26.Status}, {(list26.Json?["result"]?["tools"]?.AsArray().Count is int count ? count + " tools" : "no tools")}{ErrorOf(list26)}, session ID {(list26.SessionId is null ? "absent" : "present")}");
    Reply who26 = await mcp.PostAsync(mcp.Request2026("tools/call", new JsonObject { ["name"] = "s19_whoami", ["arguments"] = new JsonObject() }), null, Mcp.Protocol2026);
    rows.Info("S10b", $"[{mode}] s19_whoami as 2026-07-28", $"{who26.Status}: {who26.Text}{ErrorOf(who26)}");
    Reply sel26 = await mcp.PostAsync(mcp.Request2026("tools/call", new JsonObject { ["name"] = "s19_selected", ["arguments"] = new JsonObject() }), null, Mcp.Protocol2026);
    rows.Info("S10c", $"[{mode}] s19_selected as 2026-07-28 (another caller)", $"{sel26.Status}: {sel26.Text}{ErrorOf(sel26)}");

    using var other = new Mcp(endpoint, key, IPAddress.Parse("127.0.0.2"));
    Reply hijack = await other.PostAsync(mcp.Call("s19_selected"), sessionA, p);
    rows.Info("S11", $"[{mode}] session A's ID and the key from another source address (127.0.0.2)", $"{hijack.Status}: {hijack.Text}{ErrorOf(hijack)}");

    if (sessionA is not null)
    {
        Reply delete = await mcp.SendAsync(HttpMethod.Delete, null, sessionA, p);
        Reply after = await mcp.PostAsync(mcp.Request("tools/list"), sessionA, p);
        rows.Info("S12", $"[{mode}] DELETE session A, then tools/list in A", $"DELETE {delete.Status}, then {after.Status}{ErrorOf(after)}");
    }

    foreach (string line in server.Stderr.Where(l => l.StartsWith("S19 ConfigureSessionOptions", StringComparison.Ordinal)).Distinct().Take(4))
    {
        rows.Note($"[{mode}] {line}");
    }

    rows.Note($"[{mode}] ConfigureSessionOptions calls: {server.Stderr.Count(l => l.StartsWith("S19 ConfigureSessionOptions", StringComparison.Ordinal))}");
}

static async Task GateChecksAsync(string exe, string logs, Rows rows)
{
    string key = Server.NewKey();
    const string Allowed = "http://allowed.example";
    var environment = new Dictionary<string, string?>
    {
        ["S19_KEY"] = key,
        ["S19_PORT"] = "0",
        ["S19_SESSION_MODE"] = "Stateful",
        ["S19_ORIGINS"] = Allowed,
        ["S19_REQUEST_LOG"] = Path.Combine(logs, "requests-gate.jsonl"),
    };
    await using Server server = Server.Start(exe, [], environment);
    Uri endpoint = await server.WaitReadyAsync(TimeSpan.FromSeconds(30));

    // From 127.0.0.3, so that these 401s do not count against the lockout checks' source.
    using var mcp = new Mcp(endpoint, key, IPAddress.Parse("127.0.0.3"));
    string init = Mcp.Initialize(1);
    string wrong = "Bearer " + Server.NewKey();

    Reply r = await mcp.PostAsync(init, authorization: string.Empty);
    rows.Expect("G1", "POST /mcp initialize without Authorization", 401, r.Status, $"body {r.Body.Length} bytes, WWW-Authenticate {(r.WwwAuthenticate ? "present" : "absent")}");
    r = await mcp.PostAsync(init, authorization: wrong);
    rows.Expect("G2", "wrong key of the same length", 401, r.Status, $"body {r.Body.Length} bytes");
    r = await mcp.PostAsync(init, authorization: "bearer " + key);
    rows.Expect("G3", "right key, scheme written 'bearer' (exact match required)", 401, r.Status);
    r = await mcp.PostAsync(init, authorization: key);
    rows.Expect("G4", "right key without the 'Bearer ' scheme", 401, r.Status);
    r = await mcp.SendAsync(HttpMethod.Get, null, authorization: string.Empty, path: "/mcp/health");
    rows.Expect("G5", "GET /mcp/health without the key (TIA answers it)", 401, r.Status);
    r = await mcp.SendAsync(HttpMethod.Get, null, path: "/mcp/health");
    rows.Expect("G6", "GET /mcp/health with the key", 404, r.Status);
    r = await mcp.SendAsync(HttpMethod.Get, null, authorization: string.Empty, path: "/");
    rows.Expect("G7", "GET / without the key", 401, r.Status);
    r = await mcp.SendAsync(HttpMethod.Get, null, path: "/sse");
    rows.Expect("G8", "GET /sse with the key (legacy SSE is off)", 404, r.Status);
    r = await mcp.PostAsync(init, path: "/message");
    rows.Expect("G9", "POST /message with the key", 404, r.Status);
    r = await mcp.PostAsync(init);
    rows.Expect("G10", "POST /mcp initialize with the key, no Origin", 200, r.Status);

    var evil = new Dictionary<string, string> { ["Origin"] = "http://evil.example" };
    r = await mcp.PostAsync(init, headers: evil);
    rows.Expect("O1", "the key + Origin: http://evil.example (browser-like)", 403, r.Status, $"body {r.Body.Length} bytes");
    r = await mcp.PostAsync(init, authorization: string.Empty, headers: evil);
    rows.Expect("O2", "no key + Origin: http://evil.example", 403, r.Status);
    r = await mcp.PostAsync(init, headers: new Dictionary<string, string> { ["Origin"] = "null" });
    rows.Expect("O3", "the key + Origin: null (sandboxed page, file://)", 403, r.Status);
    r = await mcp.SendAsync(HttpMethod.Options, null, authorization: string.Empty, headers: new Dictionary<string, string>
    {
        ["Origin"] = "http://evil.example",
        ["Access-Control-Request-Method"] = "POST",
        ["Access-Control-Request-Headers"] = "authorization,content-type",
    });
    rows.Expect("O4", "CORS preflight OPTIONS /mcp from http://evil.example", 403, r.Status, $"Access-Control-Allow-Origin {(r.AllowOrigin is null ? "absent" : r.AllowOrigin)}");
    var allowed = new Dictionary<string, string> { ["Origin"] = Allowed };
    r = await mcp.PostAsync(init, headers: allowed);
    rows.Expect("O5", $"the key + Origin on the allow-list ({Allowed})", 200, r.Status);
    r = await mcp.PostAsync(init, authorization: string.Empty, headers: allowed);
    rows.Expect("O6", "no key + Origin on the allow-list", 401, r.Status);
    r = await mcp.PostAsync(init, authorization: "Bearer " + key + ", Bearer " + key);
    rows.Expect("G11", "the key twice in one Authorization value", 401, r.Status);

    // The default allow-list is empty: the same allowed Origin is refused by a server started without S19_ORIGINS.
    environment["S19_ORIGINS"] = null;
    environment["S19_REQUEST_LOG"] = Path.Combine(logs, "requests-gate-default-origins.jsonl");
    await using Server plain = Server.Start(exe, [], environment);
    using var mcp2 = new Mcp(await plain.WaitReadyAsync(TimeSpan.FromSeconds(30)), key);
    r = await mcp2.PostAsync(init, headers: allowed);
    rows.Expect("O7", $"default (empty) allow-list: the key + Origin {Allowed}", 403, r.Status);
}

static async Task LockoutChecksAsync(string exe, string logs, Rows rows)
{
    string key = Server.NewKey();
    var environment = new Dictionary<string, string?>
    {
        ["S19_KEY"] = key,
        ["S19_PORT"] = "0",
        ["S19_SESSION_MODE"] = "Stateful",
        ["S19_REQUEST_LOG"] = Path.Combine(logs, "requests-lockout.jsonl"),
    };
    await using Server server = Server.Start(exe, [], environment);
    Uri endpoint = await server.WaitReadyAsync(TimeSpan.FromSeconds(30));
    using var mcp = new Mcp(endpoint, key);
    using var second = new Mcp(endpoint, key, IPAddress.Parse("127.0.0.2"));
    string init = Mcp.Initialize(1);

    var statuses = new List<int>();
    for (int i = 0; i < 10; i++)
    {
        statuses.Add((await mcp.PostAsync(init, authorization: "Bearer " + Server.NewKey())).Status);
    }

    var lockedAt = Stopwatch.StartNew();
    rows.Expect("L1", "10 wrong keys from 127.0.0.1 within a minute", 10, statuses.Count(s => s == 401), "count of 401: " + string.Join(",", statuses));
    Reply r = await mcp.PostAsync(init);
    rows.Expect("L2", "11th request from 127.0.0.1, now with the right key", 429, r.Status, $"body {r.Body.Length} bytes");
    r = await second.PostAsync(init);
    rows.Expect("L3", "right key from 127.0.0.2 meanwhile (the limit is per source)", 200, r.Status);
    rows.Expect("L4", "warnings logged for the lockout", 1, server.Stderr.Count(l => l.Contains("failed the key", StringComparison.Ordinal)), server.Stderr.FirstOrDefault(l => l.Contains("failed the key", StringComparison.Ordinal)) ?? string.Empty);

    // Poll with the right key: 429 does not count as a failure, so the lockout is not extended by polling.
    int status = 429;
    while (status == 429 && lockedAt.Elapsed < TimeSpan.FromSeconds(90))
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        status = (await mcp.PostAsync(init)).Status;
    }

    rows.Expect("L5", "right key from 127.0.0.1, polled every 2 s", 200, status, $"first 200 after {lockedAt.Elapsed.TotalSeconds:F0} s");
}

static string ErrorOf(Reply reply) =>
    reply.Json?["error"] is { } error ? $" (JSON-RPC error {error["code"]}: {error["message"]})" :
    reply.Status >= 400 && reply.Body.Length > 0 ? $" (body: {reply.Body[..Math.Min(160, reply.Body.Length)].ReplaceLineEndings(" ")})" : string.Empty;

static double Median(List<double> values)
{
    double[] sorted = [.. values.Order()];
    return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

/// <summary>The table the README quotes: id, check, expected, actual, result.</summary>
internal sealed class Rows
{
    private readonly List<string> _lines = ["| # | Check | Expected | Actual | |", "|---|---|---|---|---|"];
    private readonly List<string> _notes = [];

    public bool Failed { get; private set; }

    public void Expect(string id, string check, int expected, int actual, string detail = "")
    {
        bool ok = expected == actual;
        Failed |= !ok;
        _lines.Add($"| {id} | {check} | {expected} | {actual}{(detail.Length > 0 ? "; " + detail : string.Empty)} | {(ok ? "OK" : "FAIL")} |");
    }

    public void Info(string id, string check, string actual) => _lines.Add($"| {id} | {check} | (observe) | {actual.Replace("|", "\\|", StringComparison.Ordinal)} | |");

    public void Note(string note) => _notes.Add(note);

    public string Render() => string.Join(Environment.NewLine, _lines.Concat(["", .. _notes]));
}
