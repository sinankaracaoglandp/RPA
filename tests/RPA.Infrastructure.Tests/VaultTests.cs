namespace RPA.Infrastructure.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Vault;

public class VaultTests
{
    // ---------------- SecureString (DPAPI) ----------------

    [Fact]
    public void SecureString_RoundTrips_PlaintextViaDpapi()
    {
        var secret = new SecureString("hunter2-P@ssw0rd");
        Assert.Equal("hunter2-P@ssw0rd", secret.Decrypt());
    }

    [Fact]
    public void SecureString_ToString_NeverExposesPlaintext()
    {
        var secret = new SecureString("topsecret");
        Assert.Equal("[SecureString]", secret.ToString());
        Assert.DoesNotContain("topsecret", secret.ToString());
    }

    [Fact]
    public void SecureString_EncryptedBytes_AreNotPlaintext()
    {
        var secret = new SecureString("topsecret");
        var bytes = secret.GetEncryptedBytes();
        var asText = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("topsecret", asText);
        // DPAPI şifreli çıktı ham plaintext'ten farklı ve daha uzundur.
        Assert.True(bytes.Length > "topsecret".Length);
    }

    [Fact]
    public void SecureString_FromEncryptedBytes_RoundTrips()
    {
        var original = new SecureString("veri-degeri");
        var restored = SecureString.FromEncryptedBytes(original.GetEncryptedBytes());
        Assert.Equal("veri-degeri", restored.Decrypt());
    }

    // ---------------- DpapiVaultImpl ----------------

    private static (DpapiVaultImpl vault, string dir) NewDpapiVault(
        ILogger<DpapiVaultImpl>? logger = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rpa-vault-tests", Guid.NewGuid().ToString("N"));
        var options = Options.Create(new VaultOptions { Dpapi = new DpapiOptions { StorePath = dir } });
        var vault = new DpapiVaultImpl(options, logger ?? new NullLogger<DpapiVaultImpl>());
        return (vault, dir);
    }

    [Fact]
    public async Task Dpapi_StoreThenGet_ReturnsSameSecret()
    {
        var (vault, dir) = NewDpapiVault();
        try
        {
            await vault.StoreSecretAsync("sap-dev-user-password", new SecureString("s3cr3t!"));
            var got = await vault.GetSecretAsync("sap-dev-user-password");
            Assert.Equal("s3cr3t!", got.Decrypt());
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Dpapi_Store_NeverWritesPlaintextToDisk()
    {
        var (vault, dir) = NewDpapiVault();
        try
        {
            await vault.StoreSecretAsync("k1", new SecureString("PLAINTEXT_MARKER_123"));
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var content = await File.ReadAllTextAsync(file);
                Assert.DoesNotContain("PLAINTEXT_MARKER_123", content);
            }
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Dpapi_Store_DoesNotLogPlaintext()
    {
        var capture = new CapturingLogger<DpapiVaultImpl>();
        var (vault, dir) = NewDpapiVault(capture);
        try
        {
            await vault.StoreSecretAsync("k1", new SecureString("PLAINTEXT_MARKER_LOG"),
                new Dictionary<string, string> { ["type"] = "SAP" });
            await vault.GetSecretAsync("k1");
            Assert.DoesNotContain(capture.Messages, m => m.Contains("PLAINTEXT_MARKER_LOG"));
            // Key ise loglanır.
            Assert.Contains(capture.Messages, m => m.Contains("k1"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Dpapi_Exists_And_Delete()
    {
        var (vault, dir) = NewDpapiVault();
        try
        {
            Assert.False(await vault.ExistsAsync("gone"));
            await vault.StoreSecretAsync("gone", new SecureString("x"));
            Assert.True(await vault.ExistsAsync("gone"));
            await vault.DeleteSecretAsync("gone");
            Assert.False(await vault.ExistsAsync("gone"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Dpapi_Get_MissingKey_Throws()
    {
        var (vault, dir) = NewDpapiVault();
        try
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => vault.GetSecretAsync("nope"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Dpapi_ListSecretsByTag_FiltersByMetadata()
    {
        var (vault, dir) = NewDpapiVault();
        try
        {
            await vault.StoreSecretAsync("a", new SecureString("1"),
                new Dictionary<string, string> { ["type"] = "SAP" });
            await vault.StoreSecretAsync("b", new SecureString("2"),
                new Dictionary<string, string> { ["type"] = "Web" });
            await vault.StoreSecretAsync("c", new SecureString("3"),
                new Dictionary<string, string> { ["type"] = "SAP" });

            var sap = (await vault.ListSecretsByTagAsync("type=SAP")).OrderBy(x => x).ToList();
            Assert.Equal(new[] { "a", "c" }, sap);

            var byValue = (await vault.ListSecretsByTagAsync("Web")).ToList();
            Assert.Equal(new[] { "b" }, byValue);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Dpapi_Overwrite_UpdatesValue()
    {
        var (vault, dir) = NewDpapiVault();
        try
        {
            await vault.StoreSecretAsync("k", new SecureString("old"));
            await vault.StoreSecretAsync("k", new SecureString("new"));
            Assert.Equal("new", (await vault.GetSecretAsync("k")).Decrypt());
        }
        finally { Cleanup(dir); }
    }

    // ---------------- HashiCorpVaultClient (mock HTTP) ----------------

    private static HashiCorpVaultClient NewHashiClient(
        MockHttpHandler handler, HashiCorpOptions? opts = null,
        ILogger<HashiCorpVaultClient>? logger = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://vault.test/") };
        var options = Options.Create(new VaultOptions
        {
            Type = "HashiCorp",
            HashiCorp = opts ?? new HashiCorpOptions
            {
                Url = "https://vault.test",
                Token = "test-token",
                Mount = "secret",
                MaxRetries = 3,
                RetryBaseDelayMs = 1,
            },
        });
        return new HashiCorpVaultClient(http, options, logger ?? new NullLogger<HashiCorpVaultClient>());
    }

    [Fact]
    public async Task HashiCorp_Store_PostsToDataEndpoint_WithToken()
    {
        var handler = new MockHttpHandler();
        handler.OnRequest = req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Contains("v1/secret/data/mykey", req.RequestUri!.ToString());
            Assert.Equal("test-token", req.Headers.GetValues("X-Vault-Token").Single());
            return Json(HttpStatusCode.OK, "{\"data\":{\"version\":1}}");
        };

        var client = NewHashiClient(handler);
        await client.StoreSecretAsync("mykey", new SecureString("pw"));
        Assert.True(handler.CallCount >= 1);
    }

    [Fact]
    public async Task HashiCorp_Store_SendsPlaintextValue_ButDoesNotLogIt()
    {
        var handler = new MockHttpHandler();
        string? sentBody = null;
        handler.OnRequestAsync = async req =>
        {
            sentBody = await req.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, "{\"data\":{}}");
        };
        var capture = new CapturingLogger<HashiCorpVaultClient>();
        var client = NewHashiClient(handler, logger: capture);

        await client.StoreSecretAsync("k", new SecureString("SENT_PLAINTEXT"));

        // Değer Vault'a gönderilir (sunucu at-rest şifreler)...
        Assert.Contains("SENT_PLAINTEXT", sentBody);
        // ...ama loglara asla düşmez.
        Assert.DoesNotContain(capture.Messages, m => m.Contains("SENT_PLAINTEXT"));
    }

    [Fact]
    public async Task HashiCorp_Get_ParsesValueFromKvV2Response()
    {
        var handler = new MockHttpHandler();
        handler.OnRequest = req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            var body = new JObject
            {
                ["data"] = new JObject
                {
                    ["data"] = new JObject { ["value"] = "retrieved-pw" },
                    ["metadata"] = new JObject { ["version"] = 1 },
                },
            };
            return Json(HttpStatusCode.OK, body.ToString());
        };

        var client = NewHashiClient(handler);
        var secret = await client.GetSecretAsync("mykey");
        Assert.Equal("retrieved-pw", secret.Decrypt());
    }

    [Fact]
    public async Task HashiCorp_Get_MissingKey_Throws()
    {
        var handler = new MockHttpHandler
        {
            OnRequest = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var client = NewHashiClient(handler);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => client.GetSecretAsync("nope"));
    }

    [Fact]
    public async Task HashiCorp_Exists_TrueOn200_FalseOn404()
    {
        var okHandler = new MockHttpHandler
        {
            OnRequest = _ => Json(HttpStatusCode.OK, "{\"data\":{\"data\":{\"value\":\"x\"}}}"),
        };
        var notFoundHandler = new MockHttpHandler
        {
            OnRequest = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

        Assert.True(await NewHashiClient(okHandler).ExistsAsync("k"));
        Assert.False(await NewHashiClient(notFoundHandler).ExistsAsync("k"));
    }

    [Fact]
    public async Task HashiCorp_RetriesOnServerError_ThenSucceeds()
    {
        var handler = new MockHttpHandler();
        var calls = 0;
        handler.OnRequest = _ =>
        {
            calls++;
            return calls < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(HttpStatusCode.OK, "{\"data\":{\"data\":{\"value\":\"ok\"}}}");
        };

        var client = NewHashiClient(handler);
        var secret = await client.GetSecretAsync("k");
        Assert.Equal("ok", secret.Decrypt());
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task HashiCorp_Delete_UsesMetadataEndpoint()
    {
        var handler = new MockHttpHandler();
        HttpRequestMessage? seen = null;
        handler.OnRequest = req => { seen = req; return new HttpResponseMessage(HttpStatusCode.NoContent); };

        var client = NewHashiClient(handler);
        await client.DeleteSecretAsync("k");

        Assert.Equal(HttpMethod.Delete, seen!.Method);
        Assert.Contains("v1/secret/metadata/k", seen.RequestUri!.ToString());
    }

    // ---------------- Helpers ----------------

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* test temizliği — yut */ }
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? OnRequest { get; set; }
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnRequestAsync { get; set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (OnRequestAsync is not null) return await OnRequestAsync(request);
            if (OnRequest is not null) return OnRequest(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
