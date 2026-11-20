using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>
/// EPLAN windows as Windows reports them, recorded live on EPLAN 2025.0.3.23616 (api-notes A172), and a save prompt
/// that must never be closed. Owners refer to <see cref="FakeEplan.MainFrame"/> (100) and to <see cref="Splash"/>.
/// </summary>
internal static class EplanTestWindows
{
    public static EplanWindow Splash { get; } = new(200, "SplashWnd", string.Empty, Enabled: false, Owner: 0, []);

    /// <summary>"Mensajes del sistema" on a Spanish EPLAN: the title is localized and plays no part in recognising it.</summary>
    public static EplanWindow SystemMessages { get; } = new(301, "#32770", "Mensajes del sistema", Enabled: true, Owner: FakeEplan.MainFrame.Handle,
        [new(1011, "BCGPGridCtrl:2980000:8:10003:10"), new(1076, "Edit"), new(1112, "Button"), new(1012, "Button"), new(2, "Button")]);

    /// <summary>"Seleccionar licencia": also a dialog with a BCGP grid and a Cancel button, owned by the splash screen.</summary>
    public static EplanWindow Licence { get; } = new(302, "#32770", "Seleccionar licencia", Enabled: true, Owner: Splash.Handle,
        [new(1014, "Edit"), new(1011, "Edit"), new(1096, "ComboBox"), new(1235, "BCGPGridCtrl:ce5d0000:8:10003:10"),
         new(1083, "BCGPGridCtrl:ce5d0000:8:10003:10"), new(2, "ScrollBar"), new(1163, "Button"), new(1090, "Edit"), new(1, "Button"), new(2, "Button")]);

    /// <summary>A question with consequences: whether to save, owned by the main frame like the system messages window.</summary>
    public static EplanWindow SavePrompt { get; } = new(303, "#32770", "EPLAN", Enabled: true, Owner: FakeEplan.MainFrame.Handle,
        [new(65535, "Static"), new(6, "Button"), new(7, "Button"), new(2, "Button")]);
}
