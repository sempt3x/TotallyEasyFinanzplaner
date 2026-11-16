using System.ComponentModel;
using TotallyEasyFinanzplaner.Models;
using TotallyEasyFinanzplaner.Services;

namespace TotallyEasyFinanzplaner;

/// <summary>Anzeigemodell für eine Kalenderzelle.</summary>
public sealed class DayCell(DayInfo info, bool isCurrentMonth, bool isToday, bool isSelected) : INotifyPropertyChanged
{
    private const int MaxItems = 3;
    private bool _isSelected = isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DayInfo Info { get; } = info;
    public DateOnly Date => Info.Date;
    public bool IsCurrentMonth { get; } = isCurrentMonth;
    public bool IsToday { get; } = isToday;
    public bool IsWeekend => BusinessDays.IsWeekend(Date);
    /// <summary>Wochenende oder Feiertag – an diesen Tagen wird nicht gebucht.</summary>
    public bool IsNonBusinessDay => !Info.IsBusinessDay;
    public string HolidayText => Info.Holiday ?? "";
    public bool IsHoliday => Info.Holiday is not null;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string DayText => Date.Day.ToString();
    public string BalanceText => Info.Balance is { } b ? Money.Format(b) : "";
    public bool IsNegative => Info.Balance < 0;
    public bool HasOneTime => Info.HasOneTime;
    public IEnumerable<DayItem> VisibleItems => Info.Items.Take(MaxItems);
    public string MoreText => Info.Items.Count > MaxItems ? $"+ {Info.Items.Count - MaxItems} weitere" : "";

    public string ToolTipText
    {
        get
        {
            var lines = new List<string> { Date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d. MMMM yyyy", Money.De) };
            if (Info.Holiday is { } h) lines.Add($"Feiertag: {h} – keine Buchungen");
            else if (IsWeekend) lines.Add("Wochenende – keine Buchungen");
            lines.AddRange(Info.Deferred.Select(i => $"→ {i.Name} wird am {i.Date:dd.MM.} gebucht"));
            lines.AddRange(Info.Items.Select(i => $"{i.AmountText}  {i.Name}{(i.IsOneTime ? " (einmalig)" : "")}" +
                                                  (i.IsShifted ? $" – planmäßig {i.Nominal:dd.MM.}" : "")));
            if (Info.Balance is { } b) lines.Add($"Kontostand am Tagesende: {Money.Format(b)}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
