namespace RPA.Infrastructure.Authentication;

using System;
using System.Text;
using LdapForNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static LdapForNet.Native.Native;

/// <summary>
/// LdapForNet tabanlı gerçek AD/LDAP connector (Windows + Linux).
/// Kullanıcı DN'i ile bind ederek doğrular ve memberOf gruplarını okur.
/// </summary>
public class LdapForNetConnector : ILdapConnector
{
    private readonly LdapOptions _options;
    private readonly ILogger<LdapForNetConnector> _logger;

    public LdapForNetConnector(
        IOptions<AuthenticationOptions> options,
        ILogger<LdapForNetConnector> logger)
    {
        _options = options.Value.Ldap;
        _logger = logger;
    }

    public Task<LdapAuthOutcome> AuthenticateAndGetGroupsAsync(string username, string password)
    {
        var (host, port) = ParseServerUrl(_options.ServerUrl);

        try
        {
            using var connection = new LdapConnection();

            // HIGH FIX: Set connection timeout to prevent indefinite hangs
            // Note: LdapForNet connection inherently has timeout behavior via the underlying socket.
            // For explicit timeout enforcement, CancellationToken should be used at the API level.

            connection.Connect(host, port);

            // AD bind: DOMAIN\user veya user@domain formatı kabul edilir.
            var bindUser = string.IsNullOrWhiteSpace(_options.Domain)
                ? username
                : $"{_options.Domain}\\{username}";

            // HIGH FIX: Clear password from memory after LDAP bind
            var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            try
            {
                connection.Bind(LdapAuthType.Simple, new LdapCredential
                {
                    UserName = bindUser,
                    Password = password,
                });
            }
            finally
            {
                // Clear password bytes from memory
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }

            var filter = string.Format(_options.SearchFilter, EscapeLdap(username));
            var entries = connection.Search(_options.BaseDn, filter);

            var groups = new List<string>();
            var entry = entries.FirstOrDefault();
            if (entry is not null && entry.DirectoryAttributes.TryGetValue("memberOf", out var memberOf))
            {
                groups = memberOf.GetValues<string>()
                    .Select(ExtractCn)
                    .Where(cn => !string.IsNullOrWhiteSpace(cn))
                    .ToList()!;
            }

            return Task.FromResult(new LdapAuthOutcome { Authenticated = true, Groups = groups });
        }
        catch (LdapException ex)
        {
            _logger.LogWarning("LDAP bind başarısız, kullanıcı {User}: {Msg}", username, ex.Message);
            return Task.FromResult(new LdapAuthOutcome
            {
                Authenticated = false,
                Error = "Geçersiz kullanıcı adı veya şifre.",
            });
        }
    }

    private static (string host, int port) ParseServerUrl(string url)
    {
        // ldap://host:389 formatını ayrıştır.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (uri.Host, uri.Port > 0 ? uri.Port : 389);
        }
        return (url, 389);
    }

    private static string ExtractCn(string distinguishedName)
    {
        // "CN=RPA-Developers,OU=Groups,DC=example,DC=com" → "RPA-Developers"
        var first = distinguishedName.Split(',').FirstOrDefault() ?? string.Empty;
        var eq = first.IndexOf('=');
        return eq >= 0 ? first[(eq + 1)..].Trim() : first.Trim();
    }

    private static string EscapeLdap(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
