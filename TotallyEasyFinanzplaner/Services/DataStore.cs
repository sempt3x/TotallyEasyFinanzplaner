using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TotallyEasyFinanzplaner.Models;
using IOPath = System.IO.Path;

namespace TotallyEasyFinanzplaner.Services;

/// <summary>Ein geöffneter Finanzplan: Inhalt, Speicherort und (falls verschlüsselt) der Schlüssel.</summary>
public sealed class PlanDocument
{
    public required string FilePath { get; set; }
    public required FinanceData Data { get; init; }
    /// <summary>Gesetzt, wenn der Plan mit Passwort (AES-256) gespeichert wird.</summary>
    public PlanKey? Key { get; set; }
    public bool IsEncrypted => Key is not null;
    public string Name => DataStore.DisplayName(FilePath);

    /// <summary>Hinweis nach einer automatischen Umstellung des Dateiformats.</summary>
    public string? LoadInfo { get; set; }
    /// <summary>Gesetzt, wenn die Datei defekt war und gesichert wurde.</summary>
    public string? LoadWarning { get; set; }
}

/// <summary>
/// Lesen und Schreiben der Finanzpläne. Ein Plan ist eine JSON-Datei – oder, mit Passwort, eine mit AES-256
/// verschlüsselte Datei (<see cref="PlanCrypto"/>). Der Standardplan ist daten.json im Programmdatenordner:
/// Liegt eine daten.json neben der .exe, wird diese benutzt (portabel), sonst %APPDATA%\TotallyEasyFinanzplaner
/// (ein alter Ordner %APPDATA%\EinAusgabenPlaner wird beim ersten Start dorthin übernommen).
/// </summary>
public static class DataStore
{
    public const string FileName = "daten.json";
    public const string PlanExtension = ".finanzplan";
    public const string FileFilter = "Finanzplan (*.finanzplan)|*.finanzplan|JSON-Datei (*.json)|*.json";
    public const string OpenFilter = "Finanzpläne (*.finanzplan; *.json)|*.finanzplan;*.json|Alle Dateien (*.*)|*.*";

    /// <summary>Ordner für Einstellungen und den Standardplan.</summary>
    public static string FolderPath { get; } =
        File.Exists(IOPath.Combine(AppContext.BaseDirectory, FileName))
            ? AppContext.BaseDirectory
            : AppDataFolder();

    private static string AppDataFolder()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = IOPath.Combine(appData, "TotallyEasyFinanzplaner");
        var legacy = IOPath.Combine(appData, "EinAusgabenPlaner");   // Ordnername vor der Umbenennung
        if (!Directory.Exists(folder) && Directory.Exists(legacy))
        {
            try { Directory.Move(legacy, folder); }
            catch (IOException) { return legacy; }
            catch (UnauthorizedAccessException) { return legacy; }
        }
        return folder;
    }

    public static string DefaultPlanPath => IOPath.Combine(FolderPath, FileName);

    /// <summary>Vorschlag für neue Pläne: Dokumente\Finanzpläne.</summary>
    public static string DocumentsFolder =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Finanzpläne");

    public static bool IsDefaultPlan(string path) =>
        string.Equals(IOPath.GetFullPath(path), IOPath.GetFullPath(DefaultPlanPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>Anzeigename eines Plans (der Standardplan heißt intern daten.json).</summary>
    public static string DisplayName(string path) =>
        IsDefaultPlan(path) ? "Standardplan" : IOPath.GetFileNameWithoutExtension(path);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Umlaute lesbar speichern
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Öffnet einen Plan. Für verschlüsselte Dateien ist <paramref name="password"/> nötig.
    /// Fehlt der Standardplan, wird er mit Beispieldaten angelegt.
    /// </summary>
    /// <param name="recoverIfDamaged">Bei unlesbarem Inhalt sichern und leer starten (nur für den Standardplan sinnvoll).</param>
    /// <exception cref="WrongPasswordException"/>
    /// <exception cref="InvalidDataException">Datei ist kein (lesbarer) Finanzplan.</exception>
    public static PlanDocument Open(string path, string? password = null, bool recoverIfDamaged = false)
    {
        path = IOPath.GetFullPath(path);
        if (!File.Exists(path) && IsDefaultPlan(path))
        {
            var seed = SeedData.Create();
            ClassifySalaries(seed);
            CategorizeExpenses(seed);
            var created = new PlanDocument { FilePath = path, Data = seed };
            Save(created);
            return created;
        }

        if (new FileInfo(path).Length > Limits.MaxFileBytes)
            throw new InvalidDataException($"Die Datei ist zu groß für einen Finanzplan (über {Limits.MaxFileBytes / 1024 / 1024} MB).");

        var bytes = File.ReadAllBytes(path);
        PlanKey? key = null;
        if (PlanCrypto.IsEncrypted(bytes))
        {
            if (password is null) throw new WrongPasswordException();
            bytes = PlanCrypto.Decrypt(bytes, password, out key);
        }

        FinanceData data;
        try
        {
            data = Parse(bytes);
        }
        catch (InvalidDataException ex) when (recoverIfDamaged)
        {
            var backup = BackupPath(path, "defekt");
            File.Copy(path, backup, overwrite: true);
            return new PlanDocument
            {
                FilePath = path, Data = new FinanceData(), Key = key,
                LoadWarning = $"Die Datei {path} konnte nicht gelesen werden ({ex.Message}).\n\n" +
                              $"Eine Sicherung wurde unter {backup} abgelegt. Es wird mit leeren Daten gestartet.",
            };
        }

        var doc = new PlanDocument { FilePath = path, Data = data, Key = key };
        if (data.Version < FinanceData.CurrentVersion) Migrate(doc);
        return doc;
    }

    /// <summary>Legt einen leeren Plan an und speichert ihn sofort.</summary>
    public static PlanDocument Create(string path, PlanKey? key)
    {
        var doc = new PlanDocument { FilePath = IOPath.GetFullPath(path), Data = new FinanceData(), Key = key };
        Save(doc);
        return doc;
    }

    public static void Save(PlanDocument doc)
    {
        var folder = IOPath.GetDirectoryName(doc.FilePath)!;
        Directory.CreateDirectory(folder);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc.Data, Options);
        if (doc.Key is { } key) bytes = PlanCrypto.Encrypt(bytes, key);

        // Erst vollständig in eine temporäre Datei schreiben, dann ersetzen – so bleibt bei Abbruch die alte Datei heil
        var tmp = doc.FilePath + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, doc.FilePath, overwrite: true);
        }
        catch
        {
            // Bei Fehler (z. B. Zieldatei gesperrt) keine halbe .tmp-Datei liegen lassen
            try { File.Delete(tmp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dann eben nicht */ }
            throw;
        }
    }

    /// <summary>Sicherungskopien eines Plans (vor Formatumstellung bzw. von defekten Dateien) im selben Ordner.</summary>
    public static IReadOnlyList<string> FindBackups(string path)
    {
        var folder = IOPath.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder)) return [];
        var name = IOPath.GetFileNameWithoutExtension(path);
        return [.. Directory.EnumerateFiles(folder, name + ".vor-v*").Concat(Directory.EnumerateFiles(folder, name + ".defekt-*"))];
    }

    private static string BackupPath(string path, string reason) =>
        IOPath.Combine(IOPath.GetDirectoryName(path)!,
            $"{IOPath.GetFileNameWithoutExtension(path)}.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}{IOPath.GetExtension(path)}");

    private static FinanceData Parse(byte[] utf8)
    {
        var span = utf8.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble)) span = span[3..];

        try
        {
            // Nur Dateien öffnen, die wirklich wie ein Finanzplan aussehen – sonst würde z. B. eine beliebige
            // JSON-Datei beim Umstellen des Formats überschrieben.
            bool hasVersion;
            using (var doc = JsonDocument.Parse(span.ToArray()))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !(doc.RootElement.TryGetProperty("entries", out _) || doc.RootElement.TryGetProperty("startDate", out _)))
                    throw new InvalidDataException("Die Datei ist keine Finanzplan-Datei.");
                hasVersion = doc.RootElement.TryGetProperty("version", out _);
            }

            var data = JsonSerializer.Deserialize<FinanceData>(span, Options) ?? throw new InvalidDataException("Die Datei ist leer.");
            // Ohne Versionsangabe würde der Standardwert (aktuelle Version) greifen und nie umgestellt –
            // eine solche Datei stammt aus der ersten Programmversion
            if (!hasVersion) data.Version = 1;
            if (data.Version > FinanceData.CurrentVersion)
                throw new InvalidDataException("Die Datei wurde mit einer neueren Programmversion erstellt.");
            data.Entries ??= [];
            data.Categories ??= [];

            // Unplausible Werte (von Hand bearbeitet oder manipuliert) würden sonst das Programm lahmlegen
            // (Prognose über Jahrtausende) oder beim Rechnen zum Überlauf und Absturz führen
            if (!Limits.IsValidDate(data.StartDate))
                throw new InvalidDataException($"Der Stichtag {data.StartDate:dd.MM.yyyy} liegt außerhalb von {Limits.DateRangeText}.");
            if (Math.Abs(data.StartBalance) > Limits.MaxAmount)
                throw new InvalidDataException("Der Startsaldo ist unplausibel groß.");
            if (data.Entries.Count > Limits.MaxEntries)
                throw new InvalidDataException($"Die Datei enthält mehr als {Limits.MaxEntries:N0} Buchungen.");
            if (data.HolidayState is { } state && !Enum.IsDefined(state))
                data.HolidayState = null; // unbekanntes Bundesland: nur bundesweite Feiertage

            foreach (var e in data.Entries)
            {
                e.SkippedDates ??= [];
                e.Name ??= "";
                // Unbekannte Werte (z. B. von Hand bearbeitet) würden sonst erst beim Rechnen abstürzen
                if (!Enum.IsDefined(e.Type) || !Enum.IsDefined(e.Recurrence) || !Enum.IsDefined(e.Weekend))
                    throw new InvalidDataException($"Der Eintrag „{e.Name}“ enthält eine unbekannte Art, Wiederholung oder Wochenendregel.");
                if (!Limits.IsValidDate(e.StartDate) || e.EndDate is { } end && !Limits.IsValidDate(end))
                    throw new InvalidDataException($"Der Eintrag „{e.Name}“ enthält ein Datum außerhalb von {Limits.DateRangeText}.");
                if (Math.Abs(e.Amount) > Limits.MaxAmount)
                    throw new InvalidDataException($"Der Betrag von „{e.Name}“ ist unplausibel groß.");
                if (e.SkippedDates.Count > Limits.MaxSkippedDates)
                    throw new InvalidDataException($"Der Eintrag „{e.Name}“ hat zu viele ausgesetzte Termine.");
                // Ausgesetzte Termine außerhalb des Bereichs sind harmlos (z. B. im Kalender weit vorgeblättert) – verwerfen
                e.SkippedDates.RemoveAll(s => !Limits.IsValidDate(s));
                if (e.DayOfMonth is { } day) e.DayOfMonth = Math.Clamp(day, 1, 31);
                if (e.DueMonth is { } month) e.DueMonth = Math.Clamp(month, 1, 12);
                e.Amount = Math.Abs(e.Amount);
                // Ältere Dateien: verschobenen Tag → planmäßigen Termin (mit den Feiertagen dieses Plans rechnen)
                var activeState = BusinessDays.State;
                BusinessDays.State = data.HolidayState;
                try { e.NormalizeSkippedDates(); }
                finally { BusinessDays.State = activeState; }
            }
            return data;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    /// <summary>Dateiformat aktualisieren – vorher die alte Datei sichern.</summary>
    private static void Migrate(PlanDocument doc)
    {
        var data = doc.Data;
        var backup = BackupPath(doc.FilePath, $"vor-v{FinanceData.CurrentVersion}");
        File.Copy(doc.FilePath, backup, overwrite: true);
        var notes = new List<string>();

        if (data.Version < 2) // Gehälter (mit Job) von sonstigen Einnahmen trennen
        {
            int count = ClassifySalaries(data);
            if (count > 0)
                notes.Add($"• {count} Einnahme(n) wurden anhand des Namens als Gehalt erkannt und den Jobs " +
                          "\"Hauptjob\" bzw. \"Nebenjob\" zugeordnet.");
        }
        if (data.Version < 3) // Ausgaben-Kategorien
        {
            int count = CategorizeExpenses(data);
            int rest = data.Entries.Count(e => e.Type == EntryType.Ausgabe && e.Category is null);
            notes.Add($"• Ausgaben haben jetzt Kategorien. {count} Ausgabe(n) wurden anhand des Namens vorsortiert" +
                      (rest > 0 ? $", {rest} stehen unter \"{Texts.NoCategory}\"." : "."));
        }

        data.Version = FinanceData.CurrentVersion;
        Save(doc);
        doc.LoadInfo = "Die Daten wurden auf das neue Format umgestellt:\n\n" + string.Join("\n", notes) +
                       "\n\nBitte unter \"Buchungen\" prüfen und bei Bedarf im Bearbeiten-Dialog anpassen.\n\n" +
                       $"Sicherung der alten Datei: {backup}";
    }

    /// <summary>Stuft Einnahmen mit "Gehalt"/"Lohn"/"Nebenjob" im Namen als Gehalt ein. Gibt die Anzahl zurück.</summary>
    private static int ClassifySalaries(FinanceData data)
    {
        int count = 0;
        foreach (var e in data.Entries.Where(e => e.Type == EntryType.Einnahme))
        {
            var name = e.Name.ToLowerInvariant();
            string? job = name.Contains("nebenjob") ? "Nebenjob"
                : name.Contains("gehalt") || name.Contains("lohn") ? "Hauptjob"
                : null;
            if (job is null) continue;
            e.Type = EntryType.Gehalt;
            e.Job ??= job;
            count++;
        }
        return count;
    }

    /// <summary>Stichwörter → Kategorie. Reihenfolge zählt: der erste Treffer gewinnt.</summary>
    private static readonly (string Category, string[] Keywords)[] CategoryRules =
    [
        ("Versicherungen", ["versicherung", "ergo", "bgv", "haftpflicht", "allianz", "huk"]),
        ("Kredite & Finanzierung", ["kredit", "bauspar", "lbb", "spk", "schufa", "rückzahlung", "darlehen", "rate"]),
        ("Abos & Software", ["netflix", "spotify", "discord", "twitch", "apple", "google", "onedrive", "claude", "chatgpt",
                             "jetbrains", "twitter", "github", "paramount", "proton", "server", "netcup", "ovh", "prime", "disney"]),
        ("Kommunikation", ["handy", "telefon", "internet", "mobilfunk", "telekom", "vodafone", "o2"]),
        ("Steuern & Gebühren", ["steuer", "gebühr", "rundfunk"]),
        ("Mobilität", ["kfz", "auto", "tank", "bahn", "atu", "reifen", "werkstatt"]),
        ("Wohnen", ["miete", "strom", "gas", "nebenkosten", "wohnung"]),
        ("Freizeit & Gesundheit", ["fitness", "sport", "gym", "apotheke"]),
        ("Familie", ["mutter", "vater", "kind", "unterhalt", "familie"]),
        ("Sparen & Rücklagen", ["notgroschen", "spar", "rücklage", "depot", "etf"]),
        ("Shopping", ["paypal", "otto", "mediamarkt", "alternate", "amazon", "iphone", "tablet"]),
    ];

    /// <summary>Ordnet Ausgaben ohne Kategorie anhand ihres Namens einer Kategorie zu. Gibt die Anzahl zurück.</summary>
    private static int CategorizeExpenses(FinanceData data)
    {
        int count = 0;
        foreach (var e in data.Entries.Where(e => e.Type == EntryType.Ausgabe && string.IsNullOrWhiteSpace(e.Category)))
        {
            var name = e.Name.ToLowerInvariant();
            var match = CategoryRules.FirstOrDefault(r => r.Keywords.Any(name.Contains)).Category;
            if (match is null) continue;
            e.Category = match;
            count++;
        }
        return count;
    }
}
