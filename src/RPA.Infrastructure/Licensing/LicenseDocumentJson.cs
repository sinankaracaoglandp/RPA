using System.Text.Json;
using RPA.Domain.Licensing;

namespace RPA.Infrastructure.Licensing;

/// <summary>
/// Imzali lisans belgesinin JSON gosterimi. Bu, belgeyi ayristiran TEK kaynaktir: hem
/// kalicilastirilan (DB) belge hem de import ucundan gelen dosya buradan gecer — iki ayri
/// ayristirici, kanonik yuke alan eklendiginde sessizce ayrisirdi.
/// Alan adlari buyuk/kucuk harf duyarsiz eslenir (elle duzenlenmis lisans dosyalari icin).
/// </summary>
public static class LicenseDocumentJson
{
    public static string Serialize(SignedLicenseDocument document) => JsonSerializer.Serialize(document);

    /// <summary>Ham JSON metnini ayristirir. Bicim hatalarinda <see cref="JsonException"/> atar.</summary>
    public static SignedLicenseDocument Deserialize(string json)
    {
        using var root = JsonDocument.Parse(json);
        return Read(root.RootElement);
    }

    /// <summary>
    /// Ayristirilmis bir JSON dugumunden belgeyi okur (import ucu govdeyi JsonElement alir).
    /// Eksik/yanlis tipli alanlar <see cref="JsonException"/>'a cevrilir — cagiran katman bunu
    /// 400'e donusturur; ham InvalidOperationException/ArgumentException 500 olurdu.
    /// </summary>
    public static SignedLicenseDocument Read(JsonElement document)
    {
        try
        {
            var payload = Property(document, "Payload");
            var features = Property(payload, "Features").EnumerateArray().Select(x => x.GetString()!).ToArray();
            return new SignedLicenseDocument(new OfflineLicensePayload(
                Property(payload, "SchemaVersion").GetInt32(), Property(payload, "LicenseId").GetString()!,
                Property(payload, "Revision").GetInt32(), Property(payload, "CustomerId").GetString()!,
                Property(payload, "CustomerName").GetString()!, Property(payload, "Edition").GetString()!,
                Property(payload, "InstallationId").GetString()!, Property(payload, "InstallationPublicKeyFingerprint").GetString()!,
                Property(payload, "MaxActivatedAgents").GetInt32(), Property(payload, "IssuedAt").GetDateTimeOffset(),
                Property(payload, "ExpiresAt").GetDateTimeOffset(), features),
                Property(document, "Signature").GetString()!, Property(document, "Algorithm").GetString()!);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // Yanlis tipli alan (GetInt32 bir string uzerinde), bos zorunlu alan (payload ctor
            // dogrulamasi) veya bozuk tarih — hepsi "belge gecersiz"dir, sunucu hatasi degil.
            throw new JsonException("Lisans belgesi gecersiz: alanlar okunamadi.", ex);
        }
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Beklenen JSON nesnesi degil: '{name}' okunamiyor.");
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        throw new JsonException($"Zorunlu alan eksik: '{name}'.");
    }
}
