namespace RPA.Agent.UISpy;

using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// UI Spy arka plan servisi (Task 4.4, Spec Bölüm 6). İmleç konumunu <see cref="UiSpyOptions.PollInterval"/>
/// (varsayılan 200 ms) aralıklarla yoklar; imleç bir SAP GUI elementinin üzerindeyse elementi Studio'ya
/// gönderir. Yalnızca <b>attended</b> modda kaydedilir (kayıt koşulu DI kurulumundadır) — böylece
/// güvenlik gereği (yetkili kullanıcı oturumu) sadece gözetimli robotlarda çalışır.
///
/// Tek bir tespit/gönderim hatası döngüyü durdurmaz; bir sonraki turda yeniden denenir.
/// Windows-only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UiSpyHostedService : BackgroundService
{
    private readonly SapGuiSpyService _spyService;
    private readonly UiSpyOptions _options;
    private readonly ILogger<UiSpyHostedService> _logger;

    public UiSpyHostedService(
        SapGuiSpyService spyService,
        IOptions<UiSpyOptions> options,
        ILogger<UiSpyHostedService> logger)
    {
        _spyService = spyService ?? throw new ArgumentNullException(nameof(spyService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UI Spy servisi başlatıldı (yoklama aralığı {Interval} ms).", _options.PollInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _spyService.DetectAndSendAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UI Spy tespit döngüsünde hata; devam ediliyor.");
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("UI Spy servisi durduruldu.");
    }
}

/// <summary>UI Spy yoklama yapılandırması (appsettings "UiSpy" bölümü).</summary>
public sealed class UiSpyOptions
{
    public const string SectionName = "UiSpy";

    /// <summary>İmleç yoklama aralığı. Varsayılan 200 ms (Spec Bölüm 6).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(200);
}
