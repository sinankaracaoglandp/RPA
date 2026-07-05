namespace RPA.Agent.Ui;

using System.Windows.Forms;
using RPA.Agent.JobList;
using RPA.Agent.Localization;

/// <summary>
/// İş listesi penceresi (Spec Bölüm 9): çalışan/tamamlanan işleri gerçek zamanlı gösterir.
/// <see cref="JobListViewModel.Changed"/> olayı arka plan iş parçacığından tetiklenebileceğinden
/// yeniden çizim her zaman <see cref="Control.Invoke(Delegate)"/> ile UI thread'ine marshal edilir.
/// </summary>
public sealed class JobListForm : Form
{
    private readonly JobListViewModel _viewModel;
    private readonly ListView _listView;

    public JobListForm(JobListViewModel viewModel, AgentLanguage language = AgentLanguage.Turkish)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Text = AgentStrings.Get("JobList.Title", language);
        Width = 720;
        Height = 400;

        _listView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        _listView.Columns.Add(AgentStrings.Get("JobList.ColumnJobId", language), 90);
        _listView.Columns.Add(AgentStrings.Get("JobList.ColumnWorkflow", language), 180);
        _listView.Columns.Add(AgentStrings.Get("JobList.ColumnStartedAt", language), 130);
        _listView.Columns.Add(AgentStrings.Get("JobList.ColumnCurrentStep", language), 220);
        _listView.Columns.Add(AgentStrings.Get("JobList.ColumnStatus", language), 90);
        Controls.Add(_listView);

        _viewModel.Changed += OnChanged;
        FormClosed += (_, _) => _viewModel.Changed -= OnChanged;

        Refresh_();
    }

    private void OnChanged()
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
            BeginInvoke(Refresh_);
        else
            Refresh_();
    }

    /// <summary>ListView'i mevcut anlık görüntüye göre yeniden doldurur (test/manuel tetikleme için public).</summary>
    public void Refresh_()
    {
        _listView.Items.Clear();
        foreach (var item in _viewModel.GetSnapshot())
        {
            var row = new ListViewItem(item.JobId.ToString()[..8]);
            row.SubItems.Add(item.WorkflowName);
            row.SubItems.Add(item.StartedUtc.ToLocalTime().ToString("g"));
            row.SubItems.Add(item.CurrentStep);
            row.SubItems.Add(item.Status.ToString());
            _listView.Items.Add(row);
        }
    }
}
