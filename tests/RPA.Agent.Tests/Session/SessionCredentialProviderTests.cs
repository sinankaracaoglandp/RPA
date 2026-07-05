namespace RPA.Agent.Tests.Session;

using Microsoft.Extensions.Options;
using Moq;
using RPA.Agent.Session;
using RPA.Domain.Interfaces;

public class SessionCredentialProviderTests
{
    private static SessionCredentialProvider Create(Mock<ICredentialVault> vault, SessionManagerOptions options)
        => new(vault.Object, Options.Create(options));

    [Fact]
    public async Task Parola_Vaulttan_Okunur_Kullanici_Configden()
    {
        var vault = new Mock<ICredentialVault>();
        vault.Setup(v => v.GetSecretAsync("key")).ReturnsAsync(new SecureString("p@ss"));
        var provider = Create(vault, new SessionManagerOptions
        {
            AutoLogonUserName = "robot",
            AutoLogonDomain = "CORP",
            AutoLogonPasswordVaultKey = "key"
        });

        var cred = await provider.GetAutoLogonCredentialAsync();

        Assert.Equal("robot", cred.UserName);
        Assert.Equal("CORP", cred.Domain);
        Assert.Equal("p@ss", cred.RevealPassword());
        vault.Verify(v => v.GetSecretAsync("key"), Times.Once);
    }

    [Fact]
    public async Task Bos_Alan_Null_Domain()
    {
        var vault = new Mock<ICredentialVault>();
        vault.Setup(v => v.GetSecretAsync(It.IsAny<string>())).ReturnsAsync(new SecureString("x"));
        var provider = Create(vault, new SessionManagerOptions
        {
            AutoLogonUserName = "robot",
            AutoLogonDomain = "",
            AutoLogonPasswordVaultKey = "key"
        });

        var cred = await provider.GetAutoLogonCredentialAsync();

        Assert.Null(cred.Domain);
    }

    [Fact]
    public async Task Kullanici_Bossa_Hata()
    {
        var provider = Create(new Mock<ICredentialVault>(), new SessionManagerOptions
        {
            AutoLogonUserName = "",
            AutoLogonPasswordVaultKey = "key"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAutoLogonCredentialAsync());
    }

    [Fact]
    public async Task Vault_Anahtari_Bossa_Hata()
    {
        var provider = Create(new Mock<ICredentialVault>(), new SessionManagerOptions
        {
            AutoLogonUserName = "robot",
            AutoLogonPasswordVaultKey = ""
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAutoLogonCredentialAsync());
    }

    [Fact]
    public void Credential_Parola_ToString_Maskeler()
    {
        var cred = new AutoLogonCredential("robot", null, new SecureString("secret123"));

        // SecureString.ToString gizli değeri açığa çıkarmaz.
        Assert.DoesNotContain("secret123", cred.ToString());
    }
}
