using System.Globalization;
using System.Windows.Data;
using TotallyEasyFinanzplaner.Models;
using TotallyEasyFinanzplaner.Services;

namespace TotallyEasyFinanzplaner;

/// <summary>
/// Kopfzeile einer Gruppe (Job bei Gehältern, Kategorie bei Ausgaben): regelmäßiger Betrag pro Monat.
/// ConverterParameter = Vorzeichen ("+" oder "−").
/// </summary>
public sealed class GroupMonthlySummary : IValueConverter
{
    public static GroupMonthlySummary Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CollectionViewGroup group) return "";
        var today = DateOnly.FromDateTime(DateTime.Today);
        var entries = group.Items.OfType<Entry>().ToList();
        var month = new DateOnly(today.Year, today.Month, 1);
        if (entries.All(e => e.IsOneTime))
            return $"{entries.Count} Einmalzahlung{(entries.Count == 1 ? "" : "en")}";
        return Recurring.Describe(entries, month, parameter as string ?? "+");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
