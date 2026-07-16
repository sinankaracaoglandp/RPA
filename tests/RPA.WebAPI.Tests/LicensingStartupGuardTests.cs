namespace RPA.WebAPI.Tests;

using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

/// <summary>
/// Vendor açık anahtarı lisans doğrulamasının GÜVEN KÖKÜDÜR. Yapılandırılmadığında uygulama
/// sessizce gömülü test anahtarına düşerse, unutulan tek bir ayar tüm lisans doğrulamasını
/// devre dışı bırakır. Bu testler koruma kurallarını sabitler: Development'ta izinli (uyarıyla),
/// Development dışında AÇILMAYI REDDEDER.
/// </summary>
public sealed class LicensingStartupGuardTests
{
    private static string NewVendorPublicKeyPem()
    {
        using var rsa = RSA.Create(3072);
        return new string(PemEncoding.Write("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDevelopment_WithoutVendorPublicKey_RefusesToStart(string environment)
    {
        using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));

        var error = Assert.Throws<InvalidOperationException>(() => app.CreateClient());

        // Hatanın gerçekten LİSANS koruması olduğunu doğrula — Production'a özgü başka bir
        // başlatma hatası (ör. JWT) testi sessizce "geçirmesin".
        Assert.Contains("Licensing:VendorPublicKeyPem", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_WithVendorPublicKey_Starts()
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Licensing:VendorPublicKeyPem", NewVendorPublicKeyPem());
        });

        // Koruma yalnızca eksik anahtara özgüdür; anahtar verildiğinde Production açılır.
        var client = app.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void Development_WithoutVendorPublicKey_StartsWithTestKey()
    {
        using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

        // Geliştirici akışı bozulmaz: test anahtarına düşer (yüksek sesle uyararak).
        var client = app.CreateClient();

        Assert.NotNull(client);
    }
}
