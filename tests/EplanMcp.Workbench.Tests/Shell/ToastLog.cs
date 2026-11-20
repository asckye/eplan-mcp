using EplanMcp.Workbench.Shell;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>Records the toasts asked for while it is alive, for tests of messages that are toasts only.</summary>
internal sealed class ToastLog : IDisposable
{
    private readonly List<(string Text, ToastTone Tone)> _heard = [];

    public ToastLog() => Toasts.Wanted += Hear;

    public IReadOnlyList<(string Text, ToastTone Tone)> Heard => _heard;

    public void Dispose() => Toasts.Wanted -= Hear;

    private void Hear(string text, ToastTone tone) => _heard.Add((text, tone));
}
