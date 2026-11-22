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
    public PaymentMethod Payment => Item.Payment;
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
    /// <summary>Zeitraum, den <see cref="_days"/> abdeckt (beim Blättern wiederverwendet, solange er reicht).</summary>
    private DateOnly _daysFrom = DateOnly.MaxValue, _daysTo = DateOnly.MinValue;
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
        PageLoans.Visibility = nav == NavLoans ? Visibility.Visible : Visibility.Collapsed;

        (PageCrumb.Text, PageTitle.Text) =
            nav == NavDashboard ? ("Übersicht", "Dashboard") :
            nav == NavCalendar ? ("Übersicht", "Kalender") :
            nav == NavEntries ? ("Verwaltung", "Buchungen") :
            nav == NavLoans ? ("Finanzierung", "Kredite & Raten") :
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
        ComputeForecast();
        ShowCalendar();
        RefreshEntries();
        RefreshLoans();
        ShowDashboard();
        StatusCount.Text = $"{EntryCountText.Format(_data.Entries.Count)}  ·  Startsaldo {Money.Format(_data.StartBalance)} am {_data.StartDate:dd.MM.yyyy}";
    }

    /// <summary>Letzter Tag der Dashboard-Prognose (12 Monate ab dem laufenden Monat).</summary>
    private static DateOnly DashboardHorizon => FirstOfMonth(Today).AddMonths(12).AddDays(-1);

    /// <summary>Tage, die der Kalender (6 Wochen) und die Prognose-Seite (12 Monate) ab dem angezeigten Monat brauchen.</summary>
    private (DateOnly From, DateOnly To, DateOnly GridStart, DateOnly YearEnd) CalendarRange()
    {
        int offset = ((int)_month.DayOfWeek + 6) % 7; // Montag = 0
        var gridStart = _month.AddDays(-offset);
        var gridEnd = gridStart.AddDays(41);
        var yearEnd = _month.AddMonths(12).AddDays(-1);
        var from = new[] { gridStart.AddDays(-1), _selected }.Min();
        var to = new[] { gridEnd, yearEnd, _selected }.Max();
        return (from, to, gridStart, yearEnd);
    }

    /// <summary>
    /// Berechnet die Tagesprognose einmal für alles, was angezeigt wird: Kalender, Prognose-Seite und Dashboard.
    /// Gerechnet wird ohnehin ab dem Stichtag – ein gemeinsamer Zeitraum kostet kaum mehr als einer.
    /// </summary>
    private void ComputeForecast() => ComputeForecast(extraMonths: 0);

    /// <param name="extraMonths">Zusätzlich im Voraus rechnen, damit beim Weiterblättern nicht jeder Monat neu rechnet.</param>
    private void ComputeForecast(int extraMonths)
    {
        var (from, to) = NeededRange();
        to = to.AddMonths(extraMonths);
        _days = Forecast.Compute(_data, from, to);
        _daysFrom = from < _data.StartDate ? from : _data.StartDate;
        _daysTo = to;
    }

    /// <summary>Tage, die Kalender, Prognose-Seite und Dashboard gerade brauchen.</summary>
    private (DateOnly From, DateOnly To) NeededRange()
    {
        var (from, to, _, _) = CalendarRange();
        var yesterday = Today.AddDays(-1);
        return (from < yesterday ? from : yesterday, to > DashboardHorizon ? to : DashboardHorizon);
    }

    /// <summary>
    /// Nach dem Blättern (Daten unverändert): Die vorhandene Prognose wiederverwenden, solange sie den Zeitraum
    /// abdeckt. Gerechnet wird immer ab dem Stichtag – sonst würde jeder Monatswechsel alles neu berechnen.
    /// </summary>
    private void EnsureForecast()
    {
        var (from, to) = NeededRange();
        if (from >= _daysFrom && to <= _daysTo) return;
        ComputeForecast(extraMonths: to > _daysTo ? 12 : 0);
    }

    private void ShowDashboard()
    {
        var today = Today;
        var horizon = DashboardHorizon;
        var days = _days;

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
        var future = days.Values.Where(d => d.Date >= today && d.Date <= horizon && d.Balance is not null).ToList();
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
            .ToList();
        // Kreditraten zählen zu "Kredite & Finanzierung"
        const string loanCategory = "Kredite & Finanzierung";
        var loanRates = Recurring.LoanRates(_data.Loans, from);
        if (loanRates > 0)
        {
            int i = rows.FindIndex(r => r.Name == loanCategory);
            if (i >= 0) rows[i] = (loanCategory, rows[i].Amount + loanRates);
            else rows.Add((loanCategory, loanRates));
        }
        rows = rows
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
         Recurring.PerMonth(_data.Entries.Where(e => e.Type == EntryType.Ausgabe), month) + Recurring.LoanRates(_data.Loans, month));

    /// <summary>Alle bisher verwendeten Arbeitgeber/Jobs (für die Auswahl im Dialog).</summary>
    private IEnumerable<string> KnownJobs => _data.Entries.Where(e => e.IsSalary && !string.IsNullOrWhiteSpace(e.Job)).Select(e => e.Job!.Trim());

    /// <summary>Alle bisher verwendeten Ausgaben-Kategorien (für die Auswahl im Dialog).</summary>
    private IEnumerable<string> KnownCategories => _data.AllCategories();

    private bool? ShowEntryDialog(Entry entry, bool isNew, bool lockType = false)
    {
        var result = new EntryDialog(entry, isNew, KnownJobs, KnownCategories, lockType) { Owner = this }.ShowDialog();

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

    /// <summary>Kalender und Prognose-Seite aus der bereits berechneten Prognose (<see cref="ComputeForecast"/>) aufbauen.</summary>
    private void ShowCalendar()
    {
        var (_, _, gridStart, yearEnd) = CalendarRange();

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

        // Wochenende/Feiertag: Hinweis, dass (und wohin) die Buchungen dieses Tages verschoben werden
        if (BusinessDays.Reason(_selected) is { } reason)
        {
            var moved = info.Deferred
                .GroupBy(i => i.Date).OrderBy(g => g.Key)
                .Select(g => $"{string.Join(", ", g.Select(i => i.Name))} → {ToDateTime(g.Key).ToString("dddd, dd.MM.", Money.De)}");
            SelHolidayText.Text = $"{reason} – an diesem Tag wird nicht gebucht. " +
                                  (info.Deferred.Count == 0
                                      ? "Fällige Buchungen erfolgen am nächsten Werktag (bzw. bei \"vorziehen\" am vorherigen)."
                                      : "Planmäßige Buchungen werden verschoben: " + string.Join("; ", moved) + ".");
            SelHolidayBox.Visibility = Visibility.Visible;
        }
        else SelHolidayBox.Visibility = Visibility.Collapsed;
    }

    private DataGrid[] EntryGrids => [SalaryGrid, IncomeGrid, ExpenseGrid, OneTimeExpenseGrid];

    /// <summary>Der in einer der Listen (Gehälter/Sonstige Einnahmen/regelmäßige bzw. einmalige Ausgaben) gewählte Eintrag.</summary>
    private Entry? SelectedEntry => EntryGrids.Select(g => g.SelectedItem).OfType<Entry>().FirstOrDefault();

    private void RefreshEntries()
    {
        var selectedId = SelectedEntry?.Id;
        bool showFinished = ShowFinishedBox.IsChecked == true;
        var search = SearchBox.Text.Trim();
        bool Matches(string? s) => s?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false;

        List<Entry> ListOf(EntryType type) => _data.Entries
            .Where(e => e.Type == type && (showFinished || !e.IsFinished))
            .Where(e => search.Length == 0 || Matches(e.Name) || Matches(e.Note) || (e.IsSalary && Matches(e.Job)) || (e.Type == EntryType.Ausgabe && Matches(e.CategoryName)) || (e.Payment is not null && Matches(e.PaymentText)))
            .OrderBy(e => e.IsSalary ? e.JobName : "")
            .ThenBy(e => e.IsOneTime)
            .ThenBy(e => e.IsOneTime ? e.StartDate.DayNumber : e.DueDay)
            .ThenByDescending(e => e.Amount)
            .ToList();

        var salaries = ListOf(EntryType.Gehalt);
        var incomes = ListOf(EntryType.Einnahme);
        var allExpenses = ListOf(EntryType.Ausgabe);
        var expenses = allExpenses.Where(e => !e.IsOneTime).ToList();          // regelmäßig
        var oneTimeExpenses = allExpenses.Where(e => e.IsOneTime).OrderBy(e => e.StartDate).ToList();

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
        OneTimeExpenseGrid.ItemsSource = oneTimeExpenses;
        SalaryGrid.SelectedItem = salaries.FirstOrDefault(e => e.Id == selectedId);
        IncomeGrid.SelectedItem = incomes.FirstOrDefault(e => e.Id == selectedId);
        ExpenseGrid.SelectedItem = expenses.FirstOrDefault(e => e.Id == selectedId);
        OneTimeExpenseGrid.SelectedItem = oneTimeExpenses.FirstOrDefault(e => e.Id == selectedId);
        SalaryEmpty.Visibility = salaries.Count == 0 && search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        int jobs = salaries.Select(e => e.JobName).Distinct().Count();
        SalaryCount.Text = $"{jobs} Job{(jobs == 1 ? "" : "s")} · " + CountText(salaries);
        IncomeCount.Text = "Verkäufe, Kredite, Erstattungen … · " + CountText(incomes);
        int categories = expenses.Select(e => e.CategoryName).Distinct().Count();
        ExpenseCount.Text = $"Monatlich, vierteljährlich … · {categories} Kategorie{(categories == 1 ? "" : "n")} · {EntryCountText.Format(expenses.Count)}";

        // Einmalige Ausgaben: Summe der noch offenen (ab heute)
        var open = _data.Entries.Where(e => e.Type == EntryType.Ausgabe && e.IsOneTime && !e.IsFinished).ToList();
        OneTimeExpenseCount.Text = $"Einzelne Termine · {EntryCountText.Format(oneTimeExpenses.Count)}";
        OneTimeExpenseTotal.Text = "−" + Money.Format(open.Sum(e => e.Amount));
        OneTimeExpenseTotalSub.Text = open.Count == 0 ? "nichts mehr offen" : $"noch offen ({open.Count})";

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
        // Nur im gültigen Datumsbereich blättern – dahinter wäre jede Prognose nur länger (und ab 9999 ungültig)
        var first = FirstOfMonth(Limits.MinDate);
        var last = FirstOfMonth(Limits.MaxDate);
        month = FirstOfMonth(month);
        _month = month < first ? first : month > last ? last : month;
        EnsureForecast(); // Daten unverändert – vorhandene Prognose reicht meist, Dashboard bleibt
        ShowCalendar();
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
                Key.D5 or Key.NumPad5 => NavLoans,
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
        if (!Limits.IsValidStartDate(d))
        {
            MessageDialog.Show(this, $"Der Stichtag muss im Zeitraum {Limits.StartDateRangeText} liegen.", "Ungültiges Datum",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _suppressEvents = true;
            StartDatePicker.SelectedDate = ToDateTime(_data.StartDate);
            _suppressEvents = false;
            return;
        }
        _data.StartDate = DateOnly.FromDateTime(d);
        SaveAndRefresh();
    }

    /// <summary>Bundesland für die Feiertage gewählt – alle Buchungstage neu berechnen.</summary>
    private void StateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || !_ready) return;
        FederalState? state = Enum.TryParse<FederalState>(StateBox.SelectedValue as string, out var s) ? s : null;
        if (state == _data.HolidayState) return;
        _data.HolidayState = state;
        BusinessDays.State = state;
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
            MessageDialog.Show(this, $"Bitte einen gültigen Betrag eingeben, z. B. 1.234,56 (höchstens {Money.Plain(Limits.MaxAmount)}).", "Ungültiger Betrag",
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

        try
        {
            // Buchungen und Kreditpläne; BOM, damit Excel Umlaute erkennt
            File.WriteAllText(dialog.FileName, CsvExport.Build(_data, Today), new UTF8Encoding(true));
            StatusSaved.Text = $"Exportiert: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageDialog.Show(this, $"Export fehlgeschlagen:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ------------------------------------------------------------------ Einträge bearbeiten

    /// <param name="lockType">true bei den Buttons der Tabellen (Art fest), false im Kalender (Art wählbar).</param>
    /// <summary>
    /// Prüft die Obergrenzen aus <see cref="Limits"/> vor dem Anlegen – ein Plan darüber ließe sich sonst nicht mehr öffnen.
    /// </summary>
    private bool CanAddEntry(bool recurring)
    {
        string? error = _data.Entries.Count >= Limits.MaxEntries
            ? $"Ein Finanzplan kann höchstens {Limits.MaxEntries:N0} Buchungen enthalten."
            : recurring && _data.Entries.Count(e => !e.IsOneTime) >= Limits.MaxRecurringEntries
                ? $"Ein Finanzplan kann höchstens {Limits.MaxRecurringEntries:N0} regelmäßige Buchungen enthalten."
                : null;
        if (error is null) return true;
        MessageDialog.Show(this, error + "\n\nBitte nicht mehr benötigte Buchungen löschen.", "Obergrenze erreicht",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void AddOneTime(DateOnly date, EntryType type = EntryType.Ausgabe, bool lockType = false)
    {
        if (!CanAddEntry(recurring: false)) return;

        // An Samstagen und Sonntagen wird nicht gebucht → nächsten Werktag anbieten
        if (BusinessDays.IsWeekend(date))
        {
            var next = BusinessDays.NextOrSame(date);
            if (MessageDialog.Show(this,
                    $"{ToDateTime(date).ToString("dddd, dd.MM.yyyy", Money.De)}: An Wochenenden wird nicht gebucht.\n\n" +
                    $"Einmalzahlung stattdessen am nächsten Werktag, {ToDateTime(next).ToString("dddd, dd.MM.yyyy", Money.De)}, anlegen?",
                    "Kein Buchungstag", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            date = next;
        }

        var entry = new Entry { Recurrence = Recurrence.Einmalig, StartDate = date, Type = type };
        if (ShowEntryDialog(entry, isNew: true, lockType) == true)
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
        if (DayList.SelectedItem is not DayItem item) return;
        if (item.Loan is { } loan) ShowLoan(loan); // Kreditraten werden im Kreditplan bearbeitet
        else EditEntry(item.Entry);
    }

    private void DeleteDayItem_Click(object sender, RoutedEventArgs e)
    {
        if (DayList.SelectedItem is not DayItem item) return;
        if (item.Loan is { } loan) ExplainLoanItem(loan, "gelöscht");
        else DeleteEntry(item.Entry);
    }

    /// <summary>Zahlungen aus Kreditplänen lassen sich nicht einzeln löschen/aussetzen – dafür gibt es Sondertilgung und Ablösung.</summary>
    private void ExplainLoanItem(Loan loan, string action)
    {
        if (MessageDialog.Show(this,
                $"Diese Zahlung gehört zum {(loan.Kind == LoanKind.Kredit ? "Kredit" : "Ratenplan")} „{loan.Name}“ " +
                $"und kann nicht einzeln {action} werden.\n\n" +
                "Unter „Kredite & Raten“ lassen sich der Plan ändern, Sondertilgungen eintragen oder der Restbetrag " +
                "vorzeitig auf einmal zahlen.\n\nJetzt dorthin wechseln?",
                "Zahlung aus Kreditplan", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            ShowLoan(loan);
    }

    private void SkipDayItem_Click(object sender, RoutedEventArgs e)
    {
        if (DayList.SelectedItem is not DayItem item) return;
        if (item.Loan is { } loan)
        {
            ExplainLoanItem(loan, "ausgesetzt");
            return;
        }
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
        // Tag = "Art" oder "Art:Rhythmus", z. B. "Ausgabe:Einmalig"
        var tag = ((sender as FrameworkElement)?.Tag as string ?? "").Split(':');
        var type = Enum.TryParse<EntryType>(tag[0], out var t) ? t : EntryType.Ausgabe;
        if (tag.Length > 1 && Enum.TryParse<Recurrence>(tag[1], out var r) && r == Recurrence.Einmalig)
        {
            AddOneTime(_selected, type, lockType: true);
            return;
        }
        if (!CanAddEntry(recurring: true)) return;
        var entry = new Entry { Recurrence = Recurrence.Monatlich, StartDate = _selected, Type = type, Weekend = WeekendRule.Verschieben };
        if (ShowEntryDialog(entry, isNew: true, lockType: true) == true)
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
        // Liste bewusst nicht neu aufbauen, damit die Zeile nicht wegspringt
        ComputeForecast();
        ShowCalendar();
        ShowDashboard();
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
