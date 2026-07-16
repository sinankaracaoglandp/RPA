using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using RPA.Domain.Licensing;
using RPA.Infrastructure.Licensing;

namespace RPA.LicenseGenerator;

/// <summary>Operatore gosterilebilir, sir icermeyen uretim hatasi.</summary>
public sealed class LicenseGenerationException(string message) : Exception(message);

/// <summary>
/// Etkilesimsiz (non-interactive) uretici CLI argumanlari. Guvenli bir vendor pipeline'inda
/// calisacagi icin hicbir deger prompt ile sorulmaz; hepsi acik argumandir.
/// </summary>
public sealed record LicenseGenerationOptions
{
    private LicenseGenerationOptions() { }

    public required string RequestPath { get; init; }
    public required string OutputPath { get; init; }
    public required string KeyPath { get; init; }
    public required string KeyPasswordEnvironmentVariable { get; init; }
    public required string LicenseId { get; init; }
    public required string CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required string Edition { get; init; }
    public required int Revision { get; init; }
    public required int MaxActivatedAgents { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required IReadOnlyList<string> Features { get; init; }

    public const string Usage = """
        Kullanim:
          RPA.LicenseGenerator generate \
            --request <installation-request.json> --output <customer.lic> \
            --key <vendor-encrypted-pkcs8.pem> --key-password-env <ORTAM_DEGISKENI_ADI> \
            --license-id <id> --customer-id <id> --customer-name <ad> --edition <surum> \
            --max-agents <N> --expires <YYYY-MM-DD> [--issued <YYYY-MM-DD>] [--revision <N>] \
            [--features <A,B>]

        Parola ARGUMAN OLARAK VERILMEZ; yalnizca --key-password-env ile adi verilen ortam
        degiskeninden okunur.
        """;

    public static LicenseGenerationOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || !string.Equals(args[0], "generate", StringComparison.Ordinal))
            throw new LicenseGenerationException("Bilinmeyen komut. Beklenen: 'generate'.");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || !KnownArguments.Contains(name))
                throw new LicenseGenerationException($"Bilinmeyen arguman: {name}");
            if (index + 1 >= args.Length)
                throw new LicenseGenerationException($"{name} icin deger verilmedi.");
            if (!values.TryAdd(name, args[index + 1]))
                throw new LicenseGenerationException($"{name} birden fazla kez verildi.");
        }

        var issuedAt = ParseDate(values, "--issued", DateTimeOffset.UtcNow);
        var expiresAt = ParseDate(values, "--expires", null);
        if (expiresAt <= issuedAt)
            throw new LicenseGenerationException("--expires, --issued tarihinden sonra olmalidir.");

        var maxAgents = ParseInt(values, "--max-agents", null);
        if (maxAgents <= 0)
            throw new LicenseGenerationException("--max-agents pozitif bir tamsayi olmalidir.");

        var revision = ParseInt(values, "--revision", 1);
        if (revision <= 0)
            throw new LicenseGenerationException("--revision pozitif bir tamsayi olmalidir.");

        return new LicenseGenerationOptions
        {
            RequestPath = Required(values, "--request"),
            OutputPath = Required(values, "--output"),
            KeyPath = Required(values, "--key"),
            KeyPasswordEnvironmentVariable = Required(values, "--key-password-env"),
            LicenseId = Required(values, "--license-id"),
            CustomerId = Required(values, "--customer-id"),
            CustomerName = Required(values, "--customer-name"),
            Edition = Required(values, "--edition"),
            Revision = revision,
            MaxActivatedAgents = maxAgents,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            Features = values.TryGetValue("--features", out var features)
                ? [.. features.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                : []
        };
    }

    private static readonly HashSet<string> KnownArguments = new(StringComparer.Ordinal)
    {
        "--request", "--output", "--key", "--key-password-env", "--license-id", "--customer-id",
        "--customer-name", "--edition", "--revision", "--max-agents", "--issued", "--expires", "--features"
    };

    private static string Required(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new LicenseGenerationException($"{name} zorunludur ve bos olamaz.");

    private static int ParseInt(Dictionary<string, string> values, string name, int? fallback)
    {
        if (!values.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback ?? throw new LicenseGenerationException($"{name} zorunludur ve bos olamaz.");
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new LicenseGenerationException($"{name} bir tamsayi olmalidir.");
    }

    private static DateTimeOffset ParseDate(Dictionary<string, string> values, string name, DateTimeOffset? fallback)
    {
        if (!values.TryGetValue(name, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback ?? throw new LicenseGenerationException($"{name} zorunludur ve bos olamaz.");
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? new DateTimeOffset(value, TimeSpan.Zero)
            : throw new LicenseGenerationException($"{name} gecerli bir tarih olmalidir (YYYY-MM-DD).");
    }
}

/// <summary>
/// Kurulum talebini okur, lisans yukunu kurulum kimligine baglar ve calisan urunun
/// dogruladigi ayni kanonik baytlar uzerinden (<see cref="CanonicalLicenseSerializer"/>)
/// RSA-PSS/SHA-256 ile imzalar. Kanonik JSON burada YENIDEN YAZILMAZ — ikinci bir
/// implementasyon kayarsa runtime imzayi reddederdi.
/// </summary>
public static class LicenseGenerationService
{
    public static SignedLicenseDocument Generate(LicenseGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var request = ReadInstallationRequest(options.RequestPath);
        using var signingKey = PrivateKeyLoader.Load(options.KeyPath, options.KeyPasswordEnvironmentVariable);

        var payload = OfflineLicensePayload.Create(
            options.LicenseId, options.Revision, options.CustomerId, options.CustomerName, options.Edition,
            request.InstallationId, request.InstallationPublicKeyFingerprint, options.MaxActivatedAgents,
            options.IssuedAt, options.ExpiresAt, options.Features);

        var signature = signingKey.SignData(
            CanonicalLicenseSerializer.SerializePayload(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

        var document = new SignedLicenseDocument(payload, Convert.ToBase64String(signature));
        WriteAtomically(options.OutputPath, JsonSerializer.Serialize(document));
        return document;
    }

    private static InstallationRequestDocument ReadInstallationRequest(string path)
    {
        if (!File.Exists(path))
            throw new LicenseGenerationException($"Kurulum talebi dosyasi bulunamadi: {path}");

        InstallationRequestDocument? request;
        try
        {
            request = JsonSerializer.Deserialize<InstallationRequestDocument>(
                File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            throw new LicenseGenerationException("Kurulum talebi gecerli bir JSON belgesi degil.");
        }

        if (request is null || request.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(request.InstallationId) ||
            string.IsNullOrWhiteSpace(request.InstallationPublicKey) ||
            string.IsNullOrWhiteSpace(request.InstallationPublicKeyFingerprint) ||
            string.IsNullOrWhiteSpace(request.ProductId))
            throw new LicenseGenerationException("Kurulum talebi eksik veya desteklenmeyen surumde.");

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(request.InstallationPublicKey);
        }
        catch (FormatException)
        {
            throw new LicenseGenerationException("Kurulum talebi gecerli bir base64 acik anahtar icermiyor.");
        }

        // Parmak izini talepteki acik anahtardan yeniden hesapla: yuku, kurulumun gercek
        // anahtarina bagladigimizi dogrular (uretici kurcalanmis parmak izini imzalamamalidir).
        var expected = Convert.ToHexString(SHA256.HashData(publicKey));
        if (!string.Equals(expected, request.InstallationPublicKeyFingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new LicenseGenerationException("Kurulum talebi parmak izi, icerdigi acik anahtarla eslesmiyor.");

        return request;
    }

    private static void WriteAtomically(string path, string contents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }
}
