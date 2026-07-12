namespace RPA.Infrastructure.SAP;

using System.Runtime.Versioning;

/// <summary>
/// Bir <see cref="ISapGuiSession"/> örneği üretir. İki implementasyon:
/// <see cref="ComSapGuiSessionFactory"/> (gerçek SAP GUI Scripting COM — SAP Logon kurulu Windows
/// makinede) ve <see cref="StubSapGuiSessionFactory"/> (SAP olmayan ortam/birim testleri için
/// deterministik yer tutucu). Böylece <see cref="SapGuiSessionManager"/> COM'a doğrudan bağlı değildir.
/// </summary>
public interface ISapGuiSessionFactory
{
    /// <summary>Kimlik bilgileriyle bir SAP GUI oturumu açar (gerçek modda COM logon).</summary>
    ISapGuiSession Create(string systemId, string client, string userId, string password, string language);
}

/// <summary>
/// SAP GUI kurulumu olmayan ortamlar/birim testleri için yer tutucu fabrika —
/// <see cref="StubSapGuiSession"/> üretir (gerçek COM yok).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StubSapGuiSessionFactory : ISapGuiSessionFactory
{
    public ISapGuiSession Create(string systemId, string client, string userId, string password, string language)
        => new StubSapGuiSession(systemId, client, userId, language);
}
