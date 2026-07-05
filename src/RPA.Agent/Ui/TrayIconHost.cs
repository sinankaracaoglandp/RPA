namespace RPA.Agent.Ui;

using System.Windows.Forms;
using RPA.Agent.JobList;
using RPA.Agent.Localization;
using RPA.Agent.Prompts;
using RPA.Agent.State;
using RPA.Agent.Tray;

/// <summary>
/// Gerçek WinForms tray ikonu (NotifyIcon) + bağlam menüsü (Spec Bölüm 9). Metinleri/durumu
/// <see cref="TrayStatusPresenter"/>'dan okur; komutları ona (ve <see cref="IAgentState"/>'e) iletir.
/// <see cref="UserPromptService.PromptRaised"/> olayına abone olarak modal pencereyi UI thread'inde açar.
/// Bu sınıf ince bir bağlama katmanıdır — iş mantığı test edilen presenter/service sınıflarındadır.
/// </summary>
public sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly TrayStatusPresenter _presenter;
    private readonly IAgentState _state;
    private readonly JobListViewModel _jobList;
    private readonly UserPromptService _promptService;
    private readonly AgentLanguage _language;
    private JobListForm? _jobListForm;

    public TrayIconHost(
        TrayStatusPresenter presenter,
        IAgentState state,
        JobListViewModel jobList,
        UserPromptService promptService,
        AgentLanguage language = AgentLanguage.Turkish)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _jobList = jobList ?? throw new ArgumentNullException(nameof(jobList));
        _promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        _language = language;

        var menu = new ContextMenuStrip();
        var pauseResumeItem = new ToolStripMenuItem(_presenter.GetPauseResumeCaption());
        pauseResumeItem.Click += (_, _) =>
        {
            _presenter.TogglePause();
            pauseResumeItem.Text = _presenter.GetPauseResumeCaption();
        };
        menu.Items.Add(pauseResumeItem);

        var stopJobItem = new ToolStripMenuItem(_presenter.GetStopJobCaption());
        stopJobItem.Click += (_, _) => _state.SetCurrentJob(null);
        menu.Items.Add(stopJobItem);

        var openJobListItem = new ToolStripMenuItem(_presenter.GetOpenJobListCaption());
        openJobListItem.Click += (_, _) => ShowJobList();
        menu.Items.Add(openJobListItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem(_presenter.GetExitCaption());
        exitItem.Click += (_, _) => Application.Exit();
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
            Text = Truncate(_presenter.GetTooltip()),
        };

        _promptService.PromptRaised += OnPromptRaised;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) =>
        {
            _notifyIcon.Text = Truncate(_presenter.GetTooltip());
            pauseResumeItem.Text = _presenter.GetPauseResumeCaption();
            stopJobItem.Enabled = _presenter.CanStopJob();
        };
        _refreshTimer.Start();
    }

    private void ShowJobList()
    {
        if (_jobListForm is { IsDisposed: false })
        {
            _jobListForm.BringToFront();
            return;
        }

        _jobListForm = new JobListForm(_jobList, _language);
        _jobListForm.Show();
    }

    private void OnPromptRaised(UserPromptRequest request)
    {
        // SignalR/BaseRunner arka plan iş parçacığından tetiklenir — UI oluşturmak UI thread gerektirir.
        _notifyIcon.ContextMenuStrip?.Invoke(() =>
        {
            var form = new UserPromptForm(_promptService, request, _language);
            form.Show();
        });
    }

    private static string Truncate(string text) => text.Length <= 127 ? text : text[..127];

    public void Dispose()
    {
        _refreshTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _promptService.PromptRaised -= OnPromptRaised;
        _jobListForm?.Dispose();
    }
}
