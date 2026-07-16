using System.Text.Json;
using RPA.Domain.Licensing;

namespace RPA.Infrastructure.Licensing;

internal static class LicenseDocumentJson
{
    public static string Serialize(SignedLicenseDocument document) => JsonSerializer.Serialize(document);

    public static SignedLicenseDocument Deserialize(string json)
    {
        using var root = JsonDocument.Parse(json);
        var document = root.RootElement;
        var payload = document.GetProperty("Payload");
        var features = payload.GetProperty("Features").EnumerateArray().Select(x => x.GetString()!).ToArray();
        return new SignedLicenseDocument(new OfflineLicensePayload(
            payload.GetProperty("SchemaVersion").GetInt32(), payload.GetProperty("LicenseId").GetString()!,
            payload.GetProperty("Revision").GetInt32(), payload.GetProperty("CustomerId").GetString()!,
            payload.GetProperty("InstallationId").GetString()!, payload.GetProperty("InstallationPublicKeyFingerprint").GetString()!,
            payload.GetProperty("MaxActivatedAgents").GetInt32(), payload.GetProperty("IssuedAt").GetDateTimeOffset(),
            payload.GetProperty("ExpiresAt").GetDateTimeOffset(), features),
            document.GetProperty("Signature").GetString()!, document.GetProperty("Algorithm").GetString()!);
    }
}
