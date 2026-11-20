using System.ComponentModel;
using System.Windows;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench.Themes;

/// <summary>The two Automation Studio 4.0 brand accents: Precision Cobalt (default) and Engineering Petrol.</summary>
public enum BrandAccent
{
    Cobalt,
    Petrol,
}

/// <summary>
/// Applies the optional Petrol brand accent overlay right after <see cref="ThemeManager"/>'s palette without modifying
/// any vendored TIA file, and steps aside whenever Windows High Contrast is active.
/// </summary>
public sealed class AccentManager : INotifyPropertyChanged
{
    private static readonly string _packPrefix = $"pack://application:,,,/{typeof(AccentManager).Assembly.GetName().Name};component/Themes/";
    private static readonly Uri _petrolLight = new(_packPrefix + "Palette.Accent.Petrol.Light.xaml", UriKind.Absolute);
    private static readonly Uri _petrolDark = new(_packPrefix + "Palette.Accent.Petrol.Dark.xaml", UriKind.Absolute);

    private BrandAccent _accent = BrandAccent.Cobalt;
    private ResourceDictionary? _overlay;
    private bool _subscribed;

    private AccentManager()
    {
    }

    public static AccentManager Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public BrandAccent Accent
    {
        get => _accent;
        set
        {
            if (_accent == value)
            {
                return;
            }

            _accent = value;
            Apply();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Accent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCobalt)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPetrol)));
        }
    }

    public bool IsCobalt
    {
        get => _accent == BrandAccent.Cobalt;
        set
        {
            if (value)
            {
                Accent = BrandAccent.Cobalt;
            }
        }
    }

    public bool IsPetrol
    {
        get => _accent == BrandAccent.Petrol;
        set
        {
            if (value)
            {
                Accent = BrandAccent.Petrol;
            }
        }
    }

    public void Initialize(BrandAccent accent)
    {
        _accent = accent;
        if (!_subscribed)
        {
            ThemeManager.Current.PropertyChanged += OnThemeChanged;
            _subscribed = true;
        }

        Apply();
    }

    public void Apply()
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        System.Collections.ObjectModel.Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;
        if (_overlay is not null)
        {
            merged.Remove(_overlay);
            _overlay = null;
        }

        if (_accent == BrandAccent.Petrol && !ThemeManager.Current.EffectivelyHighContrast)
        {
            _overlay = new ResourceDictionary { Source = ThemeManager.Current.EffectivelyDark ? _petrolDark : _petrolLight };
            int insertAt = Math.Min(1, merged.Count);
            merged.Insert(insertAt, _overlay);
        }
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThemeManager.Theme) or nameof(ThemeManager.EffectivelyDark) or nameof(ThemeManager.EffectivelyHighContrast))
        {
            Apply();
        }
    }
}
