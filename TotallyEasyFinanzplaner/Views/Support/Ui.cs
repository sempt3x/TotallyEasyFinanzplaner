using System.Windows;

namespace TotallyEasyFinanzplaner;

/// <summary>
/// Angehängte Eigenschaften für die XAML-Styles.
/// <para>
/// <c>Ui.Icon</c> legt das Segoe-Fluent-Symbol eines Buttons fest. Die Styles <c>IconButton</c>,
/// <c>PrimaryIconButton</c>, <c>BrandMenuButton</c>, <c>NavItem</c> und <c>ThemeSegment</c>
/// zeigen es links neben dem Text an:
/// <code>&lt;Button Style="{StaticResource IconButton}" local:Ui.Icon="&amp;#xE74D;" Content="Löschen"/&gt;</code>
/// </para>
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);
}
