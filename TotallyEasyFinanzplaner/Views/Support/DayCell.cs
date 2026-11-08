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
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

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
            lines.AddRange(Info.Items.Select(i => $"{i.AmountText}  {i.Name}{(i.IsOneTime ? " (einmalig)" : "")}"));
            if (Info.Balance is { } b) lines.Add($"Kontostand am Tagesende: {Money.Format(b)}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
