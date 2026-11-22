using System.Windows;
using System.Windows.Controls;
using TotallyEasyFinanzplaner.Models;

namespace TotallyEasyFinanzplaner;

/// <summary>Kreditplan bzw. Ratenzahlung anlegen/bearbeiten. Schreibt erst bei "Speichern" in das übergebene Objekt.</summary>
public partial class LoanDialog : Window
{
    private readonly Loan _target;
    private readonly bool _isNew;
    private bool _ready;
    /// <summary>Gesetzt, während Rate bzw. Anzahl aus dem jeweils anderen Feld berechnet werden.</summary>
    private bool _syncing;

    public LoanDialog(Loan target, bool isNew)
    {
        InitializeComponent();
        _target = target;
        _isNew = isNew;

        KindBox.ItemsSource = Enum.GetValues<LoanKind>().Select(k => new Option(k, Texts.Of(k))).ToList();
        WeekendBox.ItemsSource = new[] { WeekendRule.Verschieben, WeekendRule.Vorziehen }.Select(w => new Option(w, Texts.Of(w))).ToList();
        PaymentBox.ItemsSource = Texts.PaymentOptions;

        KindBox.SelectedValue = target.Kind;
        NameBox.Text = target.Name;
        LenderBox.Text = target.Lender ?? "";
        AmountBox.Text = target.Amount > 0 ? Money.Plain(target.Amount) : "";
        InterestBox.Text = target.InterestRate.ToString("0.###", Money.De);
        RateBox.Text = target.Rate > 0 ? Money.Plain(target.Rate) : "";
        FirstRatePicker.SelectedDate = target.FirstRateDate.ToDateTime(TimeOnly.MinValue);
        WeekendBox.SelectedValue = target.Weekend == WeekendRule.Vorziehen ? WeekendRule.Vorziehen : WeekendRule.Verschieben;
        PaymentBox.SelectedValue = target.PaymentKind;
        HasPayoutBox.IsChecked = target.PayoutDate.HasValue;
        PayoutPicker.SelectedDate = (target.PayoutDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        NoteBox.Text = target.Note ?? "";

        _ready = true;
        UpdateUi();
        Loaded += (_, _) => NameBox.Focus();
    }

    private LoanKind SelectedKind => KindBox.SelectedValue is LoanKind k ? k : LoanKind.Kredit;
    private WeekendRule SelectedWeekend => WeekendBox.SelectedValue is WeekendRule w ? w : WeekendRule.Verschieben;
    private PaymentMethod SelectedPayment => PaymentBox.SelectedValue is PaymentMethod p ? p : PaymentMethod.Konto;

    /// <summary>Sollzins in Prozent; leer = 0 (zinsfrei).</summary>
    private static bool TryInterest(string text, out decimal value)
    {
        value = 0;
        if (!string.IsNullOrWhiteSpace(text) && !Money.TryParse(text.Replace("%", ""), out value)) return false;
        return value >= 0 && value <= Limits.MaxInterestRate;
    }

    private static bool TryCount(string text, out int count) =>
        int.TryParse(text.Trim(), out count) && count is > 0 and <= LoanSchedule.MaxRates;

    /// <summary>Kreditplan mit den aktuellen Eingaben (für Zusammenfassung und Prüfung) oder null, wenn etwas fehlt.</summary>
    private Loan? BuildProbe()
    {
        if (!Money.TryParse(AmountBox.Text, out var amount) || amount <= 0) return null;
        if (!TryInterest(InterestBox.Text, out var interest)) return null;
        if (!Money.TryParse(RateBox.Text, out var rate) || rate <= 0) return null;
        if (FirstRatePicker.SelectedDate is not { } first || !Limits.IsValidDate(first)) return null;
        return new Loan
        {
            Kind = SelectedKind,
            Amount = amount,
            InterestRate = interest,
            Rate = rate,
            FirstRateDate = DateOnly.FromDateTime(first),
            Weekend = SelectedWeekend,
            // Bestehende Sondertilgungen und Ablösung fließen in die Vorschau ein
            SpecialPayments = _target.SpecialPayments,
            PayoffDate = _target.PayoffDate,
            PayoffAmount = _target.PayoffAmount,
        };
    }

    private void UpdateUi()
    {
        if (!_ready) return;
        bool credit = SelectedKind == LoanKind.Kredit;
        HeaderText.Text = _isNew ? (credit ? "Neuer Kredit" : "Neue Ratenzahlung")
                                 : (credit ? "Kredit bearbeiten" : "Ratenzahlung bearbeiten");
        Title = HeaderText.Text;
        HeaderGlyph.Text = credit ? "" : "";
        LenderLabel.Text = credit ? "Bank / Kreditgeber" : "Händler / Anbieter";
        AmountLabel.Text = credit ? "Kreditbetrag (€)" : "Finanzierter Betrag (€)";
        PayoutLabel.Visibility = PayoutPanel.Visibility = credit ? Visibility.Visible : Visibility.Collapsed;
        PayoutPicker.IsEnabled = HasPayoutBox.IsChecked == true;
        SummaryText.Text = Summary(BuildProbe());
    }

    private static string Summary(Loan? probe)
    {
        if (probe is null)
            return "Betrag, monatliche Rate (oder Anzahl der Raten) und den Termin der ersten Rate eintragen – " +
                   "der Tilgungsplan wird daraus berechnet und automatisch im Kalender gebucht.";
        if (CheckRate(probe) is { } problem) return problem;

        var rows = LoanSchedule.Build(probe);
        var rates = rows.Where(r => r.Kind == LoanRowKind.Rate).ToList();
        var paid = rows.Where(r => r.Kind != LoanRowKind.Auszahlung).ToList();
        if (paid.Count == 0) return "Keine Zahlungen.";
        var last = paid[^1];
        decimal interest = paid.Sum(r => r.Interest);
        var text = $"{rates.Count} Raten à {Money.Format(probe.Rate)}";
        if (rates.Count > 1 && rates[^1].Payment != probe.Rate) text += $", letzte Rate {Money.Format(rates[^1].Payment)}";
        text += $". Letzte Zahlung am {last.Nominal:dd.MM.yyyy}" +
                (last.Kind == LoanRowKind.Abloesung ? " (vorzeitige Ablösung)" : "") + ".";
        text += probe.InterestRate > 0
            ? $"\nZinsen gesamt {Money.Format(interest)} · zurückzuzahlen insgesamt {Money.Format(paid.Sum(r => r.Payment))}."
            : $"\nZinsfrei · zurückzuzahlen insgesamt {Money.Format(paid.Sum(r => r.Payment))}.";
        return text;
    }

    /// <summary>Fehlermeldung, wenn die Rate zu klein ist (deckt die Zinsen nicht bzw. Laufzeit über 50 Jahre).</summary>
    private static string? CheckRate(Loan probe)
    {
        var firstInterest = LoanSchedule.MonthlyInterest(probe.Amount, probe.InterestRate);
        if (probe.Rate <= firstInterest)
            return $"Die Rate von {Money.Format(probe.Rate)} deckt nicht einmal die monatlichen Zinsen ({Money.Format(firstInterest)}) – " +
                   "der Kredit würde nie getilgt. Bitte eine höhere Rate eintragen.";
        var rows = LoanSchedule.Build(probe);
        var last = rows.LastOrDefault(r => r.Kind != LoanRowKind.Auszahlung);
        if (last is not null && last.Remaining > 0 && last.Kind == LoanRowKind.Rate)
            return $"Mit dieser Rate wäre der Kredit erst nach über {LoanSchedule.MaxRates / 12} Jahren getilgt. Bitte eine höhere Rate eintragen.";
        return null;
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        // Neue Ratenzahlung: Zahlungsweg "Ratenkauf" vorschlagen (solange nichts anderes gewählt ist)
        if (_isNew && ReferenceEquals(sender, KindBox))
        {
            if (SelectedKind == LoanKind.Ratenzahlung && SelectedPayment == PaymentMethod.Konto) PaymentBox.SelectedValue = PaymentMethod.Ratenkauf;
            else if (SelectedKind == LoanKind.Kredit && SelectedPayment == PaymentMethod.Ratenkauf) PaymentBox.SelectedValue = PaymentMethod.Konto;
        }
        UpdateUi();
    }

    private void Date_Changed(object? sender, SelectionChangedEventArgs e) => UpdateUi();

    /// <summary>Betrag oder Zins geändert: Ist eine Anzahl Raten eingetragen, die Rate neu berechnen.</summary>
    private void Amount_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        if (TryCount(CountBox.Text, out _)) ApplyCount();
        UpdateUi();
    }

    private void Count_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        ApplyCount();
        UpdateUi();
    }

    /// <summary>Rate von Hand geändert: Die Anzahl gilt dann nicht mehr.</summary>
    private void Rate_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        if (!_syncing)
        {
            _syncing = true;
            CountBox.Text = "";
            _syncing = false;
        }
        UpdateUi();
    }

    private void ApplyCount()
    {
        if (!TryCount(CountBox.Text, out var count) || !Money.TryParse(AmountBox.Text, out var amount) || amount <= 0
            || !TryInterest(InterestBox.Text, out var interest))
            return;
        _syncing = true;
        RateBox.Text = Money.Plain(LoanSchedule.RateFor(amount, interest, count));
        _syncing = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string? error = null;
        var name = NameBox.Text.Trim();
        DateOnly? payout = null;

        if (name.Length == 0) error = "Bitte eine Bezeichnung eingeben (z. B. Autokredit, Fernseher).";
        else if (!Money.TryParse(AmountBox.Text, out var amount) || amount <= 0)
            error = "Bitte den Betrag eingeben (z. B. 12.000), höchstens " + Money.Plain(Limits.MaxAmount) + ".";
        else if (!TryInterest(InterestBox.Text, out var interest))
            error = $"Bitte den Sollzins in Prozent eingeben (0 bis {Limits.MaxInterestRate:0}), z. B. 5,9 – bei zinsfreier Finanzierung 0.";
        else if (!Money.TryParse(RateBox.Text, out var rate) || rate <= 0)
            error = "Bitte die monatliche Rate eingeben – oder rechts die Anzahl der Raten, dann wird die Rate berechnet.";
        else if (FirstRatePicker.SelectedDate is not { } first) error = "Bitte den Termin der ersten Rate wählen.";
        else if (!Limits.IsValidDate(first)) error = $"Das Datum muss zwischen {Limits.DateRangeText} liegen.";
        else
        {
            if (SelectedKind == LoanKind.Kredit && HasPayoutBox.IsChecked == true)
            {
                if (PayoutPicker.SelectedDate is not { } p) error = "Bitte das Datum der Auszahlung wählen oder den Haken entfernen.";
                else if (!Limits.IsValidDate(p)) error = $"Das Datum muss zwischen {Limits.DateRangeText} liegen.";
                else if (p.Date > first.Date) error = "Die Auszahlung muss vor der ersten Rate liegen.";
                else payout = DateOnly.FromDateTime(p);
            }
            if (error is null && BuildProbe() is { } probe) error = CheckRate(probe);

            if (error is null)
            {
                _target.Kind = SelectedKind;
                _target.Name = name;
                _target.Lender = string.IsNullOrWhiteSpace(LenderBox.Text) ? null : LenderBox.Text.Trim();
                _target.Amount = amount;
                _target.InterestRate = interest;
                _target.Rate = rate;
                _target.FirstRateDate = DateOnly.FromDateTime(first);
                _target.Weekend = SelectedWeekend;
                _target.Payment = SelectedPayment == PaymentMethod.Konto ? null : SelectedPayment;
                _target.PayoutDate = payout;
                _target.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
                DialogResult = true;
                return;
            }
        }

        MessageDialog.Show(this, error, "Eingabe prüfen", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
