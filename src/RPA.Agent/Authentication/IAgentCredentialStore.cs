namespace RPA.Agent.Authentication;

/// <summary>
/// Ajanin uzun omurlu credential'ini korumali sekilde saklar (Task 5, tasarim "Agent activation").
/// GUVENLIK: credential asla appsettings.json'da tutulmaz, loglanmaz veya mesajlarda gosterilmez.
/// Windows implementasyonu DPAPI LocalMachine kapsamini kullanir.
/// </summary>
public interface IAgentCredentialStore
{
    /// <summary>Saklanmis credential'i dondurur; yoksa null.</summary>
    string? TryGetCredential();

    /// <summary>Credential'i korumali sekilde yazar (aktivasyon veya rotasyon sonrasi).</summary>
    void SaveCredential(string credential);

    /// <summary>Saklanmis credential'i siler (deaktivasyon).</summary>
    void Clear();
}
