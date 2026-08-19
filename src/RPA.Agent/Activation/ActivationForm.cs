namespace RPA.Agent.Activation;

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RPA.Agent.Authentication;
using RPA.Agent.Configuration;

/// <summary>
/// Ajan aktivasyon penceresi — operatorun Studio'dan aldigi AgentId, InstallationId ve tek
/// kullanimlik aktivasyon kodunu ekrandan girip ajani baglamasini saglar (appsettings.json
/// duzenlemeden). Uretim yolunu (<see cref="AgentEnrollmentClient.ActivateAsync(Guid,string,string,CancellationToken)"/>)
/// dogrudan cagirir; donen credential istemci tarafindan DPAPI korumali depoya yazilir.
/// GUVENLIK: kod/credential hicbir yolda loglanmaz; ekranda yalnizca sunucunun stabil hata kodu gorunur.
/// </summary>
public sealed class ActivationForm : Form
{
    private readonly AgentEnrollmentClient _enrollment;
    private readonly AgentOptions _options;

    private readonly TextBox _orchestratorUrl = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Control };
    private readonly TextBox _machineName = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Control };
    private readonly TextBox _agentId = new() { Dock = DockStyle.Fill };
    private readonly TextBox _installationId = new() { Dock = DockStyle.Fill };
    private readonly TextBox _activationCode = new() { Dock = DockStyle.Fill };
    private readonly Button _activate = new() { Text = "Aktive Et", Height = 40, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 6, 2, 6) };

    /// <summary>Aktivasyon basariyla tamamlandiysa true (surec cikis kodu bunu yansitir).</summary>
    public bool Success { get; private set; }

    public ActivationForm(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _enrollment = services.GetRequiredService<AgentEnrollmentClient>();
        _options = services.GetRequiredService<IOptions<AgentOptions>>().Value;

        Text = "RPA Ajan Aktivasyonu";
        MinimumSize = new Size(560, 420);
        Size = new Size(600, 440);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5F);
        Padding = new Padding(14);
        AutoScroll = true;

        BuildLayout();

        _orchestratorUrl.Text = _options.OrchestratorUrl;
        _machineName.Text = _options.EffectiveMachineName;
        if (_options.AgentId != Guid.Empty) _agentId.Text = _options.AgentId.ToString();
        _installationId.Text = _options.InstallationId;

        if (string.IsNullOrWhiteSpace(_options.OrchestratorUrl))
        {
            SetStatus("Uyari: Orchestrator adresi yapilandirilmamis (Agent:OrchestratorUrl). " +
                "Kurulum sirasinda ayarlanmalidir.", warning: true);
        }
        else
        {
            SetStatus("Studio'dan aldiginiz AgentId, InstallationId ve aktivasyon kodunu girin.", warning: false);
        }
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(form, "Orchestrator adresi", _orchestratorUrl);
        AddRow(form, "Makine adı", _machineName);
        AddRow(form, "Agent kimliği (AgentId)", _agentId);
        AddRow(form, "Kurulum kimliği (InstallationId)", _installationId);
        AddRow(form, "Aktivasyon kodu", _activationCode);

        _activate.Click += OnActivateClicked;

        var statusBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        statusBox.Controls.Add(_status);

        root.Controls.Add(form, 0, 0);
        root.Controls.Add(_activate, 0, 1);
        root.Controls.Add(statusBox, 0, 2);
        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input)
    {
        var row = table.RowCount;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowCount = row + 1;
        table.Controls.Add(new Label
        {
            Text = label,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(3, 9, 10, 3),
        }, 0, row);
        input.Margin = new Padding(3, 5, 3, 5);
        table.Controls.Add(input, 1, row);
    }

    private async void OnActivateClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(_agentId.Text.Trim(), out var agentId) || agentId == Guid.Empty)
        {
            SetStatus("Agent kimliği geçerli bir GUID olmalı (Studio'da agent oluşunca gösterilir).", warning: true);
            return;
        }

        var installationId = _installationId.Text.Trim();
        var code = _activationCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(installationId))
        {
            SetStatus("Kurulum kimliği (InstallationId) zorunludur — lisans ekranında görünür.", warning: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            SetStatus("Aktivasyon kodu zorunludur (tek kullanımlık, 15 dk geçerli).", warning: true);
            return;
        }

        _activate.Enabled = false;
        Cursor = Cursors.WaitCursor;
        SetStatus("Aktive ediliyor…", warning: false);
        try
        {
            await _enrollment.ActivateAsync(agentId, installationId, code);
            Success = true;
            SetStatus("Aktive edildi. Credential korumalı depoya yazıldı; ajan artık normal başlatılabilir.", warning: false, success: true);
            _activationCode.ReadOnly = true;
            _agentId.ReadOnly = true;
            _installationId.ReadOnly = true;
            _activate.Text = "Aktive edildi ✓";
        }
        catch (Exception ex)
        {
            // Sunucunun stabil hata kodunu tasir (ornegin AGENT_LICENSE_LIMIT_REACHED,
            // ACTIVATION_CODE_EXPIRED). Kod tek kullanimliktir: basarisiz denemede Studio'dan yeni kod alin.
            SetStatus("Aktivasyon başarısız: " + ex.Message, warning: true);
            _activate.Enabled = true;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SetStatus(string message, bool warning, bool success = false)
    {
        _status.Text = message;
        _status.ForeColor = success ? Color.ForestGreen : warning ? Color.Firebrick : SystemColors.ControlText;
    }
}
