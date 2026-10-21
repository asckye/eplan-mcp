namespace EplanMcp.Spikes.S18;

/// <summary>
/// One made-up approval question. The change list carries what a hostile or broken project could put into device
/// names and comments: U+202E (right-to-left override), C0 controls, an ANSI escape, zero-width and tag characters,
/// an unpaired surrogate, embedded line breaks and a line longer than the limit.
/// </summary>
internal static class SampleRequest
{
    internal const string Tool = "eplan_device_update";
    internal const string Tier = "Write";
    internal const string Project = @"C:\Users\Public\EPLAN\Data\Projects\Demo\Line3.elk";
    internal const string Instance = "EPLAN 2026.0.3, Pro Panel, PID 12345";
    internal const string Client = "claude-code 2.1.299";
    internal const string Transport = "stdio";
    internal const string HostVersion = "0.5.0";
    internal const int HostPid = 4242;
    internal const string RequestId = "req-7f3a9c";

    internal static readonly IReadOnlyList<string> Changes =
    [
        "=A1+EB1-K1: 设备标识改为 -K1\u202Egnp.exe",
        "=A1+EB1-K2: 功能文本 \"电机\u0007启动\u001B[31m\" → \"电机启动器\"",
        "=A1+EB1-K3: 注释第一行\r\n注释第二行\n第三行",
        "=A1+EB1-K4: 名称\tMotor\u200B\u200DStarter\uFEFF",
        "=A1+EB1-K5: 隐藏的标签字符 \U000E0041\U000E0042 与孤立代理项 \uD800 结束",
        "=A1+EB1-K6: 双向隔离 \u2067abc\u2069 与 \u200F 方向标记",
        "=A1+EB1-K7: " + new string('长', 70) + new string('x', 60),
        "=A1+EB1-K8: 功能文本 \"Feeder\" → \"馈线\"",
        "=A1+EB1-K9: 功能文本 \"Pump\" → \"水泵\"",
        "=A1+EB1-K10: 功能文本 \"Fan\" → \"风机\"",
        "=A1+EB1-K11: 功能文本 \"Valve\" → \"阀门\"",
        "=A1+EB1-K12: 功能文本 \"Heater\" → \"加热器\"",
        "=A1+EB1-K13: 功能文本 \"Mixer\" → \"搅拌器\"",
        "=A1+EB1-K14: 功能文本 \"Lamp\" → \"指示灯\"",
        "=A1+EB1-K15: 功能文本 \"Horn\" → \"蜂鸣器\"",
    ];
}
