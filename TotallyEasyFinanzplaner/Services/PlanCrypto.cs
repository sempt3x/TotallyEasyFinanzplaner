using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TotallyEasyFinanzplaner.Services;

/// <summary>Das eingegebene Passwort passt nicht (oder die verschlüsselte Datei wurde verändert).</summary>
public sealed class WrongPasswordException()
    : Exception("Das Passwort ist falsch oder die Datei wurde verändert.");

/// <summary>
/// Aus dem Passwort abgeleiteter AES-256-Schlüssel. Wird für die Dauer der Sitzung im Speicher gehalten,
/// damit beim automatischen Speichern nicht jedes Mal neu (und langsam) abgeleitet werden muss.
/// Das Passwort selbst wird nicht aufbewahrt.
/// </summary>
public sealed class PlanKey
{
    internal byte[] Key { get; }
    internal byte[] Salt { get; }
    internal int Iterations { get; }

    internal PlanKey(byte[] key, byte[] salt, int iterations)
    {
        Key = key;
        Salt = salt;
        Iterations = iterations;
    }

    /// <summary>Prüft, ob <paramref name="password"/> zu diesem Schlüssel gehört (z. B. vor dem Ändern).</summary>
    public bool Matches(string password) =>
        CryptographicOperations.FixedTimeEquals(PlanCrypto.Derive(password, Salt, Iterations), Key);
}

/// <summary>
/// Verschlüsselung der Finanzplan-Dateien mit AES-256-GCM (authentifiziert: Manipulationen und falsche
/// Passwörter werden erkannt). Der Schlüssel wird mit PBKDF2-HMAC-SHA256 aus dem Passwort abgeleitet.
/// <para>Dateiaufbau:
/// <c>"FPLANENC"</c> (8) · Formatversion (1) · Iterationen (4, LE) · Salt (16) · Nonce (12) · Tag (16) · Geheimtext.
/// Die ersten 41 Bytes (Kopf bis einschließlich Nonce) sind als zusätzliche Daten mit authentifiziert.</para>
/// </summary>
public static class PlanCrypto
{
    private static ReadOnlySpan<byte> Magic => "FPLANENC"u8;
    private const byte FormatVersion = 1;
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32; // 32 Byte = AES-256
    private const int HeaderSize = 8 + 1 + 4 + SaltSize + NonceSize;

    /// <summary>Empfehlung von OWASP (2023) für PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Beim Öffnen akzeptierter Bereich (Spielraum für spätere Erhöhungen, etwa 0,7 s je Versuch).</summary>
    private const int MinIterations = 100_000, MaxIterations = 2_000_000;

    /// <summary>Erkennt eine verschlüsselte Datei an ihrer Kennung.</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.StartsWith(Magic);

    public static bool IsEncryptedFile(string path)
    {
        if (!File.Exists(path)) return false;
        using var fs = File.OpenRead(path);
        Span<byte> head = stackalloc byte[8];
        return fs.ReadAtLeast(head, 8, throwOnEndOfStream: false) == 8 && IsEncrypted(head);
    }

    /// <summary>Neuen Schlüssel mit zufälligem Salt aus einem Passwort erzeugen.</summary>
    public static PlanKey CreateKey(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new PlanKey(Derive(password, salt, DefaultIterations), salt, DefaultIterations);
    }

    internal static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeySize);

    public static byte[] Encrypt(byte[] plain, PlanKey key)
    {
        var result = new byte[HeaderSize + TagSize + plain.Length];
        var span = result.AsSpan();
        Magic.CopyTo(span);
        span[8] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(span[9..], key.Iterations);
        key.Salt.CopyTo(span[13..]);
        var nonce = span.Slice(13 + SaltSize, NonceSize);
        RandomNumberGenerator.Fill(nonce); // bei jedem Speichern neu

        using var aes = new AesGcm(key.Key, TagSize);
        aes.Encrypt(nonce, plain, span[(HeaderSize + TagSize)..], span.Slice(HeaderSize, TagSize), span[..HeaderSize]);
        return result;
    }

    /// <summary>Entschlüsselt eine Datei. Gibt zusätzlich den Schlüssel zurück, mit dem später gespeichert wird.</summary>
    /// <exception cref="WrongPasswordException">Passwort falsch oder Datei verändert.</exception>
    /// <exception cref="InvalidDataException">Keine gültige verschlüsselte Finanzplan-Datei.</exception>
    public static byte[] Decrypt(byte[] data, string password, out PlanKey key)
    {
        if (!IsEncrypted(data) || data.Length < HeaderSize + TagSize)
            throw new InvalidDataException("Die Datei ist keine verschlüsselte Finanzplan-Datei oder unvollständig.");
        if (data[8] != FormatVersion)
            throw new InvalidDataException($"Unbekanntes Verschlüsselungsformat (Version {data[8]}). " +
                                           "Die Datei wurde vermutlich mit einer neueren Programmversion erstellt.");

        int iterations = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(9));
        // Obergrenze: Eine manipulierte Datei könnte sonst jeden Passwortversuch sekundenlang dauern lassen
        if (iterations is < MinIterations or > MaxIterations)
            throw new InvalidDataException("Der Dateikopf ist beschädigt.");

        var salt = data.AsSpan(13, SaltSize).ToArray();
        var derived = Derive(password, salt, iterations);
        var span = data.AsSpan();
        var plain = new byte[data.Length - HeaderSize - TagSize];
        try
        {
            using var aes = new AesGcm(derived, TagSize);
            aes.Decrypt(span.Slice(13 + SaltSize, NonceSize), span[(HeaderSize + TagSize)..],
                        span.Slice(HeaderSize, TagSize), plain, span[..HeaderSize]);
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(derived);
            throw new WrongPasswordException();
        }

        key = new PlanKey(derived, salt, iterations);
        return plain;
    }
}
