using System.Globalization;
using System.Text.Json;
using RPA.Domain.Licensing;

namespace RPA.Infrastructure.Licensing;

/// <summary>
/// Imza altina giren kanonik JSON. Ozellik sirasi SABITTIR ve asagidaki yazma sirasiyla
/// tanimlanir: schemaVersion, licenseId, revision, customerId, customerName, edition,
/// installationId, installationPublicKeyFingerprint, maxActivatedAgents, issuedAt, expiresAt,
/// features. Yeni alan eklemek imzalanan baytlari degistirir — yalniz kontrat degisikligi ile.
/// </summary>
public static class CanonicalLicenseSerializer
{
    public static byte[] SerializePayload(OfflineLicensePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", payload.SchemaVersion);
            writer.WriteString("licenseId", payload.LicenseId);
            writer.WriteNumber("revision", payload.Revision);
            writer.WriteString("customerId", payload.CustomerId);
            writer.WriteString("customerName", payload.CustomerName);
            writer.WriteString("edition", payload.Edition);
            writer.WriteString("installationId", payload.InstallationId);
            writer.WriteString("installationPublicKeyFingerprint", payload.InstallationPublicKeyFingerprint);
            writer.WriteNumber("maxActivatedAgents", payload.MaxActivatedAgents);
            writer.WriteString("issuedAt", payload.IssuedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("expiresAt", payload.ExpiresAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteStartArray("features");
            foreach (var feature in payload.Features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                writer.WriteStringValue(feature);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
