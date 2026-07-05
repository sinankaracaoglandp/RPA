namespace RPA.Agent.Session;

using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

/// <summary>
/// <see cref="ISessionSwitcher"/> implementasyonu — <c>tscon.exe</c> ile oturumu konsola taşır.
/// Komut: <c>tscon.exe &lt;sessionId&gt; /dest:console</c>. tscon çıkışta sıfır dışı kod
/// döndürürse hata fırlatılır (oturum kilitli/erişim reddedildi vb.).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TsconSessionSwitcher : ISessionSwitcher
{
    private readonly ILogger<TsconSessionSwitcher> _logger;

    public TsconSessionSwitcher(ILogger<TsconSessionSwitcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SwitchToConsoleAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "tscon.exe",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("/dest:console");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("tscon.exe başlatılamadı.");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"tscon.exe {sessionId} /dest:console başarısız (çıkış kodu {process.ExitCode}): {stderr.Trim()}");
        }

        _logger.LogInformation("Oturum {SessionId} konsola taşındı.", sessionId);
    }
}
