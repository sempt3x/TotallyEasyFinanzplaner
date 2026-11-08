using System.Globalization;
using System.Windows.Data;

namespace TotallyEasyFinanzplaner;

/// <summary>Anzahl für Gruppenköpfe mit richtiger Einzahl/Mehrzahl: "  ·  1 Eintrag" bzw. "  ·  3 Einträge".</summary>
public sealed class EntryCountText : IValueConverter
{
    public static EntryCountText Instance { get; } = new();

    public static string Format(int count) => count == 1 ? "1 Eintrag" : $"{count} Einträge";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n ? "  ·  " + Format(n) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
