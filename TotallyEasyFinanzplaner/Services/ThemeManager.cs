using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace TotallyEasyFinanzplaner.Services;

public enum AppTheme { System, Hell, Dunkel }

/// <summary>
/// Schaltet zwischen hellem und dunklem Erscheinungsbild um: tauscht das Fluent-Theme von WPF und die eigene
/// Farbpalette (Resources/Themes/Light.xaml bzw. Dark.xaml) aus und färbt die Fenster-Titelleisten passend ein.
/// Die Wahl wird in einstellungen.json im Datenordner gespeichert.
/// </summary>
public static class ThemeManager
{
    private const string FluentLight = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml";
    private const string FluentDark = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml";
    private const string PaletteLight = "pack://application:,,,/TotallyEasyFinanzplaner;component/Resources/Themes/Light.xaml";
    private const string PaletteDark = "pack://application:,,,/TotallyEasyFinanzplaner;component/Resources/Themes/Dark.xaml";

    public static AppTheme Selected { get; private set; } = AppTheme.System;
    public static bool IsDark { get; private set; }

    /// <summary>Wird nach jedem Wechsel ausgelöst (z. B. um Farben aus Code neu zu setzen).</summary>
    public static event Action? Changed;

    /// <summary>Beim Programmstart aufrufen: Einstellung laden, anwenden und auf Systemwechsel reagieren.</summary>
    public static void Initialize()
    {
        Selected = AppSettings.Current.Theme;

        // Jedes neu geöffnete Fenster bekommt die passende Titelleiste
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => ApplyTitleBar((Window)s)));

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Selected == AppTheme.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System, save: false));
        };

        Apply(Selected, save: false);
    }

    public static void Apply(AppTheme theme, bool save = true)
    {
        Selected = theme;
        bool dark = theme == AppTheme.Dunkel || (theme == AppTheme.System && SystemUsesDarkMode());

        var merged = Application.Current.Resources.MergedDictionaries;
        if (dark != IsDark || merged.Count < 2 || merged[1].Source is null)
        {
            IsDark = dark;
            merged[0] = new ResourceDictionary { Source = new Uri(dark ? FluentDark : FluentLight) };
            merged[1] = new ResourceDictionary { Source = new Uri(dark ? PaletteDark : PaletteLight) };
            foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        }

        if (save)
        {
            AppSettings.Current.Theme = theme;
            AppSettings.Current.Save();
        }

        Changed?.Invoke();
    }

    private static bool SystemUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    // ---- Titelleiste (Windows 10 20H1+ / Windows 11) ----

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmUseImmersiveDarkMode = 20;

    private static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
        // Titelleiste sofort neu zeichnen lassen
        if (window.IsLoaded && window.WindowState == WindowState.Normal
            && window.SizeToContent == SizeToContent.Manual && !double.IsNaN(window.Width))
        {
            window.Width += 1;
            window.Width -= 1;
        }
    }
}
