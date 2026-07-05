namespace RPA.Agent.Hosting;

using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RPA.Agent.Configuration;
using RPA.Agent.JobList;
using RPA.Agent.Prompts;
using RPA.Agent.State;
using RPA.Agent.Tray;
using RPA.Agent.Ui;
using RPA.Domain.Enums;

/// <summary>
/// Attended modda (Spec Bölüm 9) tray ikonunu bir STA (Single Threaded Apartment) iş parçacığında
/// çalıştırır — WinForms mesaj döngüsü STA gerektirir, ana host ise MTA olabilir. Unattended modda
/// (RobotMode.Unattended) hiçbir UI oluşturmaz; servis olarak sessizce çalışır.
/// </summary>
public sealed class TrayUiHostedService : IHostedService
{
    private readonly TrayStatusPresenter _presenter;
    private readonly IAgentState _state;
    private readonly JobListViewModel _jobList;
    private readonly UserPromptService _promptService;
    private readonly AgentOptions _options;
    private readonly ILogger<TrayUiHostedService> _logger;
    private Thread? _uiThread;
    private TrayIconHost? _trayIconHost;

    public TrayUiHostedService(
        TrayStatusPresenter presenter,
        IAgentState state,
        JobListViewModel jobList,
        UserPromptService promptService,
        IOptions<AgentOptions> options,
        ILogger<TrayUiHostedService> logger)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _jobList = jobList ?? throw new ArgumentNullException(nameof(jobList));
        _promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode != RobotMode.Attended || !OperatingSystem.IsWindows())
        {
            _logger.LogInformation("Tray UI başlatılmadı (Mode={Mode}, Windows={IsWindows}).", _options.Mode, OperatingSystem.IsWindows());
            return Task.CompletedTask;
        }

        _uiThread = new Thread(RunUiThread) { IsBackground = true };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        return Task.CompletedTask;
    }

    private void RunUiThread()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        _trayIconHost = new TrayIconHost(_presenter, _state, _jobList, _promptService);
        System.Windows.Forms.Application.Run();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_uiThread is null)
            return Task.CompletedTask;

        try
        {
            System.Windows.Forms.Application.Exit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray UI kapatılırken hata (yok sayıldı).");
        }
        finally
        {
            _trayIconHost?.Dispose();
        }

        return Task.CompletedTask;
    }
}
