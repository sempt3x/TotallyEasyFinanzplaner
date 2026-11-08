using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TotallyEasyFinanzplaner.Models;
using TotallyEasyFinanzplaner.Services;
using Microsoft.Win32;

namespace TotallyEasyFinanzplaner;

/// <summary>Zeile in der Liste "Nächste Buchungen" auf dem Dashboard.</summary>
public sealed record UpcomingRow(DayItem Item, decimal? BalanceAfter)
{
    public string DayText => Item.Date.Day.ToString();
    public string MonthText => Item.Date.ToDateTime(TimeOnly.MinValue).ToString("MMM", Money.De).TrimEnd('.').ToUpper();
    public string Name => Item.Name;
    public string SubText => Item.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd", Money.De) + " · " + Item.SubText;
    public string AmountText => Item.AmountText;
    public bool IsIncome => Item.IsIncome;
    public string BalanceText => BalanceAfter is { } b ? Money.Format(b) : "";
    public bool BalanceNegative => BalanceAfter < 0;
}

/// <summary>Balken in "Ausgaben nach Kategorie" auf dem Dashboard.</summary>
public sealed record CategoryRow(string Name, decimal Amount, double Share)
{
    private const double BarMaxWidth = 390; // Innenbreite der Karte
    public string AmountText => "−" + Money.Format(Amount);
    public string PercentText => $"{Share:P0}";
    public double BarWidth => Math.Max(2, Share * BarMaxWidth);
}

public partial class MainWindow : Window
{
    private PlanDocument _doc;
    private FinanceData _data => _doc.Data;
    private DateOnly _month;     // immer der 1. des angezeigten Monats
    private DateOnly _selected;
    private SortedDictionary<DateOnly, DayInfo> _days = new();
    private bool _suppressEvents;
    private bool _ready;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
    private static DateTime ToDateTime(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public MainWindow(PlanDocument doc)
    {
        InitializeComponent();
        _doc = doc;
        SyncThemeButtons();
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        PreviewMouseDown += Window_PreviewMouseDownWhileBusy;

        _ready = true;
        ShowPage(NavDashboard);
        LoadPlan(doc);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => ShowLoadNotices();

    /// <summary>Hinweise nach dem Öffnen (Datei defekt / Format umgestellt) einmalig anzeigen.</summary>
    private void ShowLoadNotices()
    {
        if (_doc.LoadWarning is { } warning)
            MessageDialog.Show(this, warning, "Datei defekt", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (_doc.LoadInfo is { } info)
        {
            MessageDialog.Show(this, info, "Daten umgestellt", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowPage(NavEntries);
        }
        _doc.LoadWarning = _doc.LoadInfo = null;
    }

    // ------------------------------------------------------------------ Navigation zwischen Seiten

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready && sender is RadioButton rb) ShowPage(rb);
    }

    private void ShowPage(RadioButton nav)
    {
        nav.IsChecked = true;
        PageDashboard.Visibility = nav == NavDashboard ? Visibility.Visible : Visibility.Collapsed;
        PageCalendar.Visibility = nav == NavCalendar ? Visibility.Visible : Visibility.Collapsed;
        PageEntries.Visibility = nav == NavEntries ? Visibility.Visible : Visibility.Collapsed;
        PageForecast.Visibility = nav == NavForecast ? Visibility.Visible : Visibility.Collapsed;

        (PageCrumb.Text, PageTitle.Text) =
            nav == NavDashboard ? ("Übersicht", "Dashboard") :
            nav == NavCalendar ? ("Übersicht", "Kalender") :
            nav == NavEntries ? ("Verwaltung", "Buchungen") :
            ("Verwaltung", "Prognose");
        PageCrumb.Text = "Totally Easy Finanzplaner  /  " + PageCrumb.Text;
    }

    private void GoToCalendar_Click(object sender, RoutedEventArgs e) => ShowPage(NavCalendar);

    // ------------------------------------------------------------------ Darstellung (hell/dunkel)

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_ready && sender is RadioButton { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var theme)
            && theme != ThemeManager.Selected)
            ThemeManager.Apply(theme);
    }

    private void SyncThemeButtons()
    {
        var target = ThemeManager.Selected switch
        {
            AppTheme.Hell => ThemeLight,
            AppTheme.Dunkel => ThemeDark,
            _ => ThemeSystem,
        };
        target.IsChecked = true;
    }

    /// <summary>Farben, die im Code gesetzt werden (Kennzahlen, Hinweis), und Diagramme neu aufbauen.</summary>
    private void OnThemeChanged()
    {
        bool wasReady = _ready;
        _ready = false; // verhindert Rückkopplung über Theme_Checked
        SyncThemeButtons();
        _ready = wasReady;
        RefreshAll();
    }

    // ------------------------------------------------------------------ Aktualisieren

    private void RefreshAll()
    {
        RefreshCalendar();
        RefreshEntries();
        RefreshDashboard();
        StatusCount.Text = $"{EntryCountText.Format(_data.Entries.Count)}  ·  Startsaldo {Money.Format(_data.StartBalance)} am {_data.StartDate:dd.MM.yyyy}";
    }

    private void RefreshDashboard()
    {
        var today = Today;
        var horizon = FirstOfMonth(today).AddMonths(12).AddDays(-1);
        var days = Forecast.Compute(_data, today.AddDays(-1), horizon);

        // Kontostand heute
        var todayInfo = days[today];
        if (todayInfo.Balance is { } tb)
        {
            ShowAmount(DashToday, tb);
            DashTodaySub.Text = todayInfo.Items.Count == 0
                ? "Keine Buchungen heute"
                : $"Heute {Money.FormatSigned(todayInfo.Net)} ({todayInfo.Items.Count} Buchung{(todayInfo.Items.Count == 1 ? "" : "en")})";
        }
        else
        {
            DashToday.Text = "–";
            DashToday.Foreground = (Brush)FindResource("MutedBrush");
            DashTodaySub.Text = $"Berechnung ab {_data.StartDate:dd.MM.yyyy}";
        }

        // Monatsende
        var stats = MonthStats.Create(_data, days, FirstOfMonth(today));
        ShowAmount(DashMonthEnd, stats.Closing);
        DashMonthEndSub.Text = $"{ToDateTime(today).ToString("MMMM", Money.De)}: +{Money.Plain(stats.Income)} / −{Money.Plain(stats.Expense)}";

        // Tiefststand und erster negativer Tag in den nächsten 12 Monaten
        var future = days.Values.Where(d => d.Date >= today && d.Balance is not null).ToList();
        var min = future.MinBy(d => d.Balance);
        ShowAmount(DashMin, min?.Balance);
        DashMinSub.Text = min is null ? "" : $"am {ToDateTime(min.Date).ToString("dddd, dd.MM.yyyy", Money.De)}";

        var firstNegative = future.FirstOrDefault(d => d.Balance < 0);
        if (firstNegative is not null)
        {
            AlertBox.Background = (Brush)FindResource("ExpenseSoftBrush");
            AlertBox.BorderBrush = (Brush)FindResource("AlertNegativeBorderBrush");
            AlertIcon.Text = "";
            AlertIcon.Foreground = (Brush)FindResource("ExpenseBrush");
            AlertText.Text = $"Liquiditätswarnung: Der Kontostand wird voraussichtlich am {firstNegative.Date:dd.MM.yyyy} negativ " +
                             $"({Money.Format(firstNegative.Balance!.Value)}). Tiefststand {Money.Format(min!.Balance!.Value)} am {min.Date:dd.MM.yyyy}.";
        }
        else
        {
            AlertBox.Background = (Brush)FindResource("IncomeSoftBrush");
            AlertBox.BorderBrush = (Brush)FindResource("AlertPositiveBorderBrush");
            AlertIcon.Text = "";
            AlertIcon.Foreground = (Brush)FindResource("IncomeBrush");
            AlertText.Text = $"Liquidität gesichert: Kein negativer Kontostand bis {horizon:dd.MM.yyyy} prognostiziert.";
        }

        // Laufende Bilanz pro Monat
        var (sal, oth, exp) = RecurringPerMonth(FirstOfMonth(today));
        ShowAmount(DashRecurring, sal + oth - exp, signed: true);
        DashRecurringSub.Text = $"Gehalt +{Money.Plain(sal)} · Sonst. +{Money.Plain(oth)} · Ausg. −{Money.Plain(exp)}";

        RefreshCategoryBars(FirstOfMonth(today));

        // Diagramm und nächste Buchungen
        DashChart.SetData(future.Select(d => (d.Date, d.Balance!.Value)));

        var upcoming = days.Values
            .Where(d => d.Date >= today && d.Date <= today.AddDays(30))
            .SelectMany(d =>
            {
                // Saldo nach jeder einzelnen Buchung des Tages
                decimal? running = d.Balance is { } b ? b - d.Net : null;
                return d.Items.Select(i => new UpcomingRow(i, running += i.Signed));
            })
            .ToList();
        UpcomingList.ItemsSource = upcoming;
        UpcomingEmpty.Visibility = upcoming.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Regelmäßige Ausgaben pro Monat je Kategorie (Monat mit laufenden Ausgaben, ab heute).</summary>
    private void RefreshCategoryBars(DateOnly month)
    {
        var expenses = _data.Entries.Where(e => e.Type == EntryType.Ausgabe).ToList();
        var (_, from) = Recurring.CurrentOrNext(expenses, month);
        var rows = expenses
            .GroupBy(e => e.CategoryName)
            .Select(g => (Name: g.Key, Amount: Recurring.PerMonth(g, from)))
            .Where(x => x.Amount > 0)
            .OrderByDescending(x => x.Amount)
            .ToList();
        decimal total = rows.Sum(r => r.Amount);

        // Höchstens 6 Balken, Rest zusammenfassen
        if (rows.Count > 6)
        {
            var rest = rows.Skip(5).Sum(r => r.Amount);
            rows = [.. rows.Take(5), ($"Weitere ({rows.Count - 5})", rest)];
        }
        CategoryBars.ItemsSource = rows
            .Select(r => new CategoryRow(r.Name, r.Amount, total == 0 ? 0 : (double)(r.Amount / total)))
            .ToList();
        CategoryTotal.Text = "−" + Money.Format(total);
        CategorySubtitle.Text = $"Regelmäßig pro Monat · {ToDateTime(from).ToString("MMMM yyyy", Money.De)}";
    }

    private void ShowAmount(TextBlock target, decimal? value, bool signed = false)
    {
        target.Text = value is { } v ? (signed ? Money.FormatSigned(v) : Money.Format(v)) : "–";
        target.Foreground = (Brush)FindResource(value < 0 ? "ExpenseBrush" : "TextBrush");
    }

    private (decimal Salary, decimal OtherIncome, decimal Expense) RecurringPerMonth(DateOnly month) =>
        (Recurring.PerMonth(_data.Entries.Where(e => e.Type == EntryType.Gehalt), month),
         Recurring.PerMonth(_data.Entries.Where(e => e.Type == EntryType.Einnahme), month),
         Recurring.PerMonth(_data.Entries.Where(e => e.Type == EntryType.Ausgabe), month));

    /// <summary>Alle bisher verwendeten Arbeitgeber/Jobs (für die Auswahl im Dialog).</summary>
    private IEnumerable<string> KnownJobs => _data.Entries.Where(e => e.IsSalary && !string.IsNullOrWhiteSpace(e.Job)).Select(e => e.Job!.Trim());

    /// <summary>Alle bisher verwendeten Ausgaben-Kategorien (für die Auswahl im Dialog).</summary>
    private IEnumerable<string> KnownCategories => _data.AllCategories();

    private bool? ShowEntryDialog(Entry entry, bool isNew)
    {
        var result = new EntryDialog(entry, isNew, KnownJobs, KnownCategories) { Owner = this }.ShowDialog();

        // Im Dialog neu eingetippte Kategorie dauerhaft als eigene Kategorie merken
        if (result == true && entry.Category is { } cat && !Texts.IsDefaultCategory(cat)
            && !_data.Categories.Contains(cat, StringComparer.CurrentCultureIgnoreCase))
            _data.Categories.Add(cat);
        return result;
    }

    private void ManageCategories_Click(object sender, RoutedEventArgs e)
    {
        new CategoryDialog(_data, () => Save()) { Owner = this }.ShowDialog();
        RefreshAll();
    }

    private void RefreshCalendar()
    {
        int offset = ((int)_month.DayOfWeek + 6) % 7; // Montag = 0
        var gridStart = _month.AddDays(-offset);
        var gridEnd = gridStart.AddDays(41);
        var yearEnd = _month.AddMonths(12).AddDays(-1);

        var from = new[] { gridStart.AddDays(-1), _selected }.Min();
        var to = new[] { gridEnd, yearEnd, _selected }.Max();
        _days = Forecast.Compute(_data, from, to);

        MonthTitle.Text = ToDateTime(_month).ToString("MMMM yyyy", Money.De);

        var cells = new List<DayCell>(42);
        for (int i = 0; i < 42; i++)
        {
            var d = gridStart.AddDays(i);
            cells.Add(new DayCell(_days[d], d.Month == _month.Month && d.Year == _month.Year, d == Today, d == _selected));
        }
        CalendarItems.ItemsSource = cells;

        var stats = MonthStats.Create(_data, _days, _month);
        ShowAmount(StatOpening, stats.Opening);
        StatIncome.Text = "+" + Money.Format(stats.Income);
        StatExpense.Text = "−" + Money.Format(stats.Expense);
        ShowAmount(StatClosing, stats.Closing);
        ShowAmount(StatMin, stats.Min);
        StatMinLabel.Text = stats.MinDate is { } md ? $"TIEFSTSTAND · {md:dd.MM.}" : "TIEFSTSTAND";

        // Prognose-Seite: 12 Monate ab angezeigtem Monat
        YearGrid.ItemsSource = Enumerable.Range(0, 12)
            .Select(i => MonthStats.Create(_data, _days, _month.AddMonths(i)))
            .ToList();
        ForecastRange.Text = $"{ToDateTime(_month).ToString("MMMM yyyy", Money.De)} – {ToDateTime(yearEnd).ToString("MMMM yyyy", Money.De)}";
        ForecastChart.SetData(_days.Values
            .Where(d => d.Date >= _month && d.Date <= yearEnd && d.Balance is not null)
            .Select(d => (d.Date, d.Balance!.Value)));

        RefreshDay();
    }

    private void RefreshDay()
    {
        var info = _days.TryGetValue(_selected, out var i) ? i : Forecast.Compute(_data, _selected, _selected)[_selected];

        SelDateTitle.Text = ToDateTime(_selected).ToString("dddd, d. MMMM yyyy", Money.De);
        if (info.Balance is { } b)
        {
            SelBalanceText.Text = $"Kontostand am Tagesende: {Money.Format(b)}";
            SelBalanceText.Foreground = (Brush)FindResource(b < 0 ? "ExpenseBrush" : "TextBrush");
        }
        else
        {
            SelBalanceText.Text = "Vor dem Startdatum – kein Kontostand berechnet.";
            SelBalanceText.Foreground = (Brush)FindResource("MutedBrush");
        }

        SelChangeText.Text = info.Items.Count == 0
            ? "Keine Buchungen an diesem Tag."
            : $"{info.Items.Count} Buchung(en), Veränderung {Money.FormatSigned(info.Net)}";
        DayList.ItemsSource = info.Items;
    }

    private DataGrid[] EntryGrids => [SalaryGrid, IncomeGrid, ExpenseGrid];

    /// <summary>Der in einer der drei Listen (Gehälter/Sonstige Einnahmen/Ausgaben) gewählte Eintrag.</summary>
    private Entry? SelectedEntry => EntryGrids.Select(g => g.SelectedItem).OfType<Entry>().FirstOrDefault();

    private void RefreshEntries()
    {
        var selectedId = SelectedEntry?.Id;
        bool showFinished = ShowFinishedBox.IsChecked == true;
        var search = SearchBox.Text.Trim();
        bool Matches(string? s) => s?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false;

        List<Entry> ListOf(EntryType type) => _data.Entries
            .Where(e => e.Type == type && (showFinished || !e.IsFinished))
            .Where(e => search.Length == 0 || Matches(e.Name) || Matches(e.Note) || (e.IsSalary && Matches(e.Job)) || (e.Type == EntryType.Ausgabe && Matches(e.CategoryName)))
            .OrderBy(e => e.IsSalary ? e.JobName : "")
            .ThenBy(e => e.IsOneTime)
            .ThenBy(e => e.IsOneTime ? e.StartDate.DayNumber : e.DueDay)
            .ThenByDescending(e => e.Amount)
            .ToList();

        var salaries = ListOf(EntryType.Gehalt);
        var incomes = ListOf(EntryType.Einnahme);
        var expenses = ListOf(EntryType.Ausgabe);

        // Gehälter nach Arbeitgeber/Job gruppieren
        var salaryView = new System.Windows.Data.ListCollectionView(salaries);
        salaryView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(Entry.JobName)));
        SalaryGrid.ItemsSource = salaryView;
        IncomeGrid.ItemsSource = incomes;

        // Ausgaben nach Kategorie gruppieren: größte Kategorie (pro Monat) zuerst, "Ohne Kategorie" zuletzt
        var month0 = FirstOfMonth(Today);
        var categoryWeight = _data.Entries.Where(e => e.Type == EntryType.Ausgabe)
            .GroupBy(e => e.CategoryName)
            .ToDictionary(g => g.Key, g => Recurring.CurrentOrNext(g, month0).Amount);
        expenses = expenses
            .OrderBy(e => e.CategoryName == Texts.NoCategory)
            .ThenByDescending(e => categoryWeight.GetValueOrDefault(e.CategoryName))
            .ThenBy(e => e.CategoryName)
            .ToList(); // OrderBy ist stabil – die Reihenfolge innerhalb der Kategorie bleibt erhalten
        var expenseView = new System.Windows.Data.ListCollectionView(expenses);
        expenseView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(Entry.CategoryName)));
        ExpenseGrid.ItemsSource = expenseView;
        SalaryGrid.SelectedItem = salaries.FirstOrDefault(e => e.Id == selectedId);
        IncomeGrid.SelectedItem = incomes.FirstOrDefault(e => e.Id == selectedId);
        ExpenseGrid.SelectedItem = expenses.FirstOrDefault(e => e.Id == selectedId);
        SalaryEmpty.Visibility = salaries.Count == 0 && search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        int jobs = salaries.Select(e => e.JobName).Distinct().Count();
        SalaryCount.Text = $"{jobs} Job{(jobs == 1 ? "" : "s")} · " + CountText(salaries);
        IncomeCount.Text = "Verkäufe, Kredite, Erstattungen … · " + CountText(incomes);
        int categories = expenses.Select(e => e.CategoryName).Distinct().Count();
        ExpenseCount.Text = $"{categories} Kategorie{(categories == 1 ? "" : "n")} · " + CountText(expenses);

        var month = FirstOfMonth(Today);
        SetMonthly(SalaryMonthly, SalaryMonthlySub, _data.Entries.Where(e => e.Type == EntryType.Gehalt), month, "+");
        SetMonthly(IncomeMonthly, IncomeMonthlySub, _data.Entries.Where(e => e.Type == EntryType.Einnahme), month, "+");
        SetMonthly(ExpenseMonthly, ExpenseMonthlySub, _data.Entries.Where(e => e.Type == EntryType.Ausgabe), month, "−");

        var (sal, oth, exp) = RecurringPerMonth(month);
        SummaryTitle.Text = $"MONATLICHE BILANZ · {ToDateTime(month).ToString("MMMM yyyy", Money.De).ToUpper()}";
        SumSalary.Text = "+" + Money.Format(sal);
        SumIncome.Text = "+" + Money.Format(oth);
        SumExpense.Text = "−" + Money.Format(exp);
        ShowAmount(SumRest, sal + oth - exp, signed: true);
    }

    /// <summary>Kartenkopf: regelmäßiger Betrag pro Monat (bzw. ab dem ersten Monat, in dem etwas läuft).</summary>
    private static void SetMonthly(TextBlock value, TextBlock sub, IEnumerable<Entry> entries, DateOnly month, string sign)
    {
        var (amount, from) = Recurring.CurrentOrNext(entries, month);
        value.Text = sign + Money.Format(amount);
        sub.Text = from > month
            ? $"pro Monat ab {ToDateTime(from).ToString("MMMM yyyy", Money.De)}"
            : "regelmäßig pro Monat";
    }

    private static string CountText(List<Entry> list)
    {
        int recurring = list.Count(e => !e.IsOneTime);
        return $"{EntryCountText.Format(list.Count)} ({recurring} regelmäßig, {list.Count - recurring} einmalig)";
    }

    private void SaveAndRefresh()
    {
        Save();
        RefreshAll();
    }

    private bool Save()
    {
        try
        {
            DataStore.Save(_doc);
            StatusSaved.Text = $"Gespeichert um {DateTime.Now:HH:mm:ss}";
            return true;
        }
        catch (Exception ex)
        {
            StatusSaved.Text = "Speichern fehlgeschlagen";
            MessageDialog.Show(this, $"Speichern fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    // ------------------------------------------------------------------ Monatsnavigation

    private void ShowMonth(DateOnly month)
    {
        _month = FirstOfMonth(month);
        RefreshCalendar();
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(-1));
    private void NextMonth_Click(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(1));

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _selected = Today;
        ShowMonth(Today);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_busy) { e.Handled = true; return; } // Schlüssel wird gerade abgeleitet
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.D)
        {
            ThemeManager.Apply(ThemeManager.IsDark ? AppTheme.Hell : AppTheme.Dunkel);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            e.Handled = true;
            SavePlanAs();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            Action? fileAction = e.Key switch
            {
                Key.N => NewPlan,
                Key.O => OpenPlan,
                Key.S => SaveNow,
                _ => null,
            };
            if (fileAction is not null)
            {
                e.Handled = true;
                fileAction();
                return;
            }

            RadioButton? nav = e.Key switch
            {
                Key.D1 or Key.NumPad1 => NavDashboard,
                Key.D2 or Key.NumPad2 => NavCalendar,
                Key.D3 or Key.NumPad3 => NavEntries,
                Key.D4 or Key.NumPad4 => NavForecast,
                _ => null,
            };
            if (nav is not null) { ShowPage(nav); e.Handled = true; return; }
            if (e.Key == Key.F)
            {
                ShowPage(NavEntries);
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }
        }

        if (e.OriginalSource is TextBox) return;
        if (e.Key == Key.PageUp) { ShowMonth(_month.AddMonths(-1)); e.Handled = true; }
        else if (e.Key == Key.PageDown) { ShowMonth(_month.AddMonths(1)); e.Handled = true; }
    }

    private int _wheelDelta;

    /// <summary>
    /// Mausrad blättert die Monate. Touchpads und fein rastende Räder liefern viele kleine Schritte –
    /// erst eine volle Raste (120) wechselt den Monat, sonst würde eine Wischbewegung viele Monate überspringen.
    /// </summary>
    private void Calendar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (e.Delta == 0) return;
        if (Math.Sign(e.Delta) != Math.Sign(_wheelDelta)) _wheelDelta = 0; // Richtungswechsel: neu sammeln
        _wheelDelta += e.Delta;

        int steps = _wheelDelta / Mouse.MouseWheelDeltaForOneLine;
        if (steps == 0) return;
        _wheelDelta -= steps * Mouse.MouseWheelDeltaForOneLine;
        ShowMonth(_month.AddMonths(-steps)); // nach oben = früherer Monat
    }

    private void YearGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (YearGrid.SelectedItem is not MonthStats stats) return;
        ShowMonth(stats.Month);
        ShowPage(NavCalendar);
    }

    private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DayCell cell }) return;

        bool sameDay = cell.Date == _selected;
        _selected = cell.Date;

        if (!cell.IsCurrentMonth)
        {
            ShowMonth(cell.Date);
            return;
        }

        foreach (var c in (IEnumerable<DayCell>)CalendarItems.ItemsSource)
            c.IsSelected = c.Date == _selected;
        RefreshDay();

        if (e.ClickCount == 2 && sameDay) AddOneTime(cell.Date);
    }

    // ------------------------------------------------------------------ Startsaldo

    private void StartDatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || StartDatePicker.SelectedDate is not { } d) return;
        _data.StartDate = DateOnly.FromDateTime(d);
        SaveAndRefresh();
    }

    private void StartBalanceBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitStartBalance();
    }

    private void StartBalanceBox_LostFocus(object sender, RoutedEventArgs e) => CommitStartBalance();

    private void CommitStartBalance()
    {
        if (Money.TryParse(StartBalanceBox.Text, out var value))
        {
            StartBalanceBox.Text = Money.Plain(value);
            if (value == _data.StartBalance) return;
            _data.StartBalance = value;
            SaveAndRefresh();
        }
        else
        {
            MessageDialog.Show(this, "Bitte einen gültigen Betrag eingeben, z. B. 1.234,56", "Ungültiger Betrag",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            StartBalanceBox.Text = Money.Plain(_data.StartBalance);
        }
    }

    // ------------------------------------------------------------------ Daten

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        // Explorer öffnen und die Datei des aktuellen Plans markieren
        var args = File.Exists(_doc.FilePath) ? $"/select,\"{_doc.FilePath}\"" : $"\"{Path.GetDirectoryName(_doc.FilePath)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Buchungen exportieren",
            Filter = "CSV-Datei (Excel)|*.csv",
            FileName = $"Buchungen_{DateTime.Today:yyyy-MM-dd}.csv",
        };
        if (dialog.ShowDialog(this) != true) return;

        // Textfelder quoten. Beginnt ein Text mit = + - @ (oder Tab/Zeilenumbruch), würde Excel ihn als Formel
        // ausführen (CSV-Injection) – ein vorangestelltes ' macht ihn zu reinem Text.
        static string Q(string? s)
        {
            s ??= "";
            if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder();
        sb.AppendLine("Art;Arbeitgeber/Job;Kategorie;Bezeichnung;Betrag;Rhythmus;Fällig;Gültig ab;Endet am;Wochenende;Notiz");
        foreach (var x in _data.Entries.OrderByDescending(x => x.Type).ThenBy(x => x.Job).ThenBy(x => x.IsOneTime).ThenBy(x => x.Name))
        {
            sb.AppendJoin(';',
                Q(x.TypeText), Q(x.IsSalary ? x.JobName : ""), Q(x.Type == EntryType.Ausgabe ? x.CategoryName : ""), Q(x.Name), x.SignedAmount.ToString("0.00", Money.De), Q(x.RecurrenceText),
                Q(x.DueText), x.StartDate.ToString("dd.MM.yyyy"), x.EndDate?.ToString("dd.MM.yyyy") ?? "",
                Q(Texts.Of(x.Weekend)), Q(x.Note));
            sb.AppendLine();
        }

        try
        {
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true)); // BOM, damit Excel Umlaute erkennt
            StatusSaved.Text = $"Exportiert: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, $"Export fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ------------------------------------------------------------------ Einträge bearbeiten

    private void AddOneTime(DateOnly date)
    {
        var entry = new Entry { Recurrence = Recurrence.Einmalig, StartDate = date };
        if (ShowEntryDialog(entry, isNew: true) == true)
        {
            _data.Entries.Add(entry);
            SaveAndRefresh();
        }
    }

    private void EditEntry(Entry entry)
    {
        if (ShowEntryDialog(entry, isNew: false) == true)
            SaveAndRefresh();
    }

    private void DeleteEntry(Entry entry)
    {
        var text = entry.IsOneTime
            ? $"Einmalzahlung \"{entry.Name}\" ({entry.AmountText}) am {entry.StartDate:dd.MM.yyyy} löschen?"
            : $"\"{entry.Name}\" ist {entry.RecurrenceText.ToLower()}. Die GESAMTE Serie löschen?\n\n" +
              "Tipp: Um nur einen Termin wegzulassen, im Kalender \"Aussetzen\" verwenden.";
        if (MessageDialog.Show(this, text, "Löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _data.Entries.Remove(entry);
        SaveAndRefresh();
    }

    private void AddOneTime_Click(object sender, RoutedEventArgs e) => AddOneTime(_selected);

    private void EditDayItem_Click(object sender, RoutedEventArgs e)
    {
        if (DayList.SelectedItem is DayItem item) EditEntry(item.Entry);
    }

    private void DeleteDayItem_Click(object sender, RoutedEventArgs e)
    {
        if (DayList.SelectedItem is DayItem item) DeleteEntry(item.Entry);
    }

    private void SkipDayItem_Click(object sender, RoutedEventArgs e)
    {
        if (DayList.SelectedItem is not DayItem item) return;
        if (item.IsOneTime)
        {
            MessageDialog.Show(this, "Einmalzahlungen können nicht ausgesetzt werden – bitte löschen.", "Hinweis",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        item.Entry.SkippedDates.Add(item.Nominal); // planmäßiger Termin, nicht der verschobene Tag
        SaveAndRefresh();
    }

    private void NewEntry_Click(object sender, RoutedEventArgs e)
    {
        var type = Enum.TryParse<EntryType>((sender as FrameworkElement)?.Tag as string, out var t) ? t : EntryType.Ausgabe;
        var entry = new Entry { Recurrence = Recurrence.Monatlich, StartDate = _selected, Type = type };
        if (ShowEntryDialog(entry, isNew: true) == true)
        {
            _data.Entries.Add(entry);
            SaveAndRefresh();
        }
    }

    private void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is { } entry) EditEntry(entry);
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry is { } entry) DeleteEntry(entry);
    }

    /// <summary>Es ist immer nur in einer der beiden Listen etwas ausgewählt.</summary>
    private void EntryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Auswahländerungen der Tag-Auswahlbox in einer Zeile ignorieren (Ereignis wird hochgereicht)
        if (!ReferenceEquals(e.OriginalSource, sender) || e.AddedItems.Count == 0) return;
        foreach (var grid in EntryGrids)
            if (!ReferenceEquals(grid, sender)) grid.UnselectAll();
    }

    /// <summary>Abbuchungstag direkt in der Liste geändert.</summary>
    private void DayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Nur echte Benutzeränderungen (nicht das initiale Setzen beim Aufbau der Zeile)
        if (sender is not ComboBox { DataContext: Entry entry, SelectedValue: int day } combo
            || e.RemovedItems.Count == 0
            || !(combo.IsDropDownOpen || combo.IsKeyboardFocusWithin)
            || entry.DueDay == day)
            return;

        entry.DueDay = day;
        Save();
        RefreshCalendar(); // Liste bewusst nicht neu aufbauen, damit die Zeile nicht wegspringt
        RefreshDashboard();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_ready) RefreshEntries();
    }

    private void ShowFinished_Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) RefreshEntries();
    }
}
