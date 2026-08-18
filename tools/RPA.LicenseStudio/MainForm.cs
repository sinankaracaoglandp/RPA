namespace RPA.LicenseStudio;

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RPA.LicenseGenerator;

/// <summary>
/// Satici lisans kesme ekrani. Tum arayuz kodla kurulur (tasarimci dosyasi yok).
/// Uretim imzalama yolunu (<see cref="LicenseGenerationService"/>) dogrudan cagirir —
/// kripto YENIDEN YAZILMAZ. Parola yalnizca surec-kapsamli bir ortam degiskenine bir an
/// icin yazilir (uretim CLI'sinin sozlesmesi geregi) ve islem biter bitmez temizlenir;
/// hicbir yere kalicilastirilmaz veya loglanmaz.
/// </summary>
public sealed class MainForm : Form
{
    // Uretim CLI'siyle ayni sozlesme: parola argumanla degil, adi verilen ortam degiskeninden okunur.
    private const string PasswordEnvVar = "RPA_LICENSE_STUDIO_PW";

    private readonly AppSettings _settings = AppSettings.Load();

    private readonly TextBox _keyPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _requestPath = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly TextBox _licenseId = new() { Dock = DockStyle.Fill };
    private readonly TextBox _customerId = new() { Dock = DockStyle.Fill };
    private readonly TextBox _customerName = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _edition = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly NumericUpDown _maxAgents = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 100000, Value = 3 };
    private readonly NumericUpDown _revision = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 1000000, Value = 1 };
    private readonly DateTimePicker _expires = new() { Dock = DockStyle.Fill, Format = DateTimePickerFormat.Short };
    private readonly TextBox _features = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputPath = new() { Dock = DockStyle.Fill };

    private readonly Button _generate = new() { Text = "Lisans Üret", Height = 40, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
    private readonly Button _openFolder = new() { Text = "Çıktı klasörünü aç", Height = 40, Enabled = false };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.Gainsboro,
        Font = new Font("Consolas", 9F),
    };

    private string? _lastOutput;

    public MainForm()
    {
        Text = "RPA Lisans Stüdyosu";
        MinimumSize = new Size(760, 640);
        Size = new Size(820, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5F);
        Padding = new Padding(12);

        _edition.Items.AddRange(["community", "professional", "enterprise"]);
        _expires.MinDate = DateTime.Today.AddDays(1);
        _expires.Value = DateTime.Today.AddYears(1);

        BuildLayout();
        WireEvents();
        ApplySettings();
        Log("Hazır. Anahtar, parola ve kurulum talebini seçip alanları doldurun, ardından \"Lisans Üret\"e basın.");
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // form alanlari
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // butonlar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // gunluk

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0, 0, 0, 8),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(form, "Satıcı anahtarı (.pem)", _keyPath, BrowseButton(BrowseKey));
        AddRow(form, "Anahtar parolası", _password, null);
        AddRow(form, "Kurulum talebi (.json)", _requestPath, RequestButtons());
        AddSeparator(form);
        AddRow(form, "Lisans No", _licenseId, null);
        AddRow(form, "Müşteri No", _customerId, null);
        AddRow(form, "Müşteri adı", _customerName, null);
        AddRow(form, "Sürüm (edition)", _edition, null);
        AddRow(form, "Agent koltuğu", _maxAgents, null);
        AddRow(form, "Revizyon", _revision, null);
        AddRow(form, "Bitiş tarihi", _expires, null);
        AddRow(form, "Özellikler (virgülle)", _features, null);
        AddSeparator(form);
        AddRow(form, "Çıktı dosyası (.lic)", _outputPath, BrowseButton(BrowseOutput));

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0, 4, 0, 8),
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _generate.Dock = DockStyle.Fill;
        _openFolder.Dock = DockStyle.Fill;
        buttons.Controls.Add(_generate, 0, 0);
        buttons.Controls.Add(_openFolder, 1, 0);

        root.Controls.Add(form, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(_log, 0, 2);
        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input, Control? trailing)
    {
        var row = table.RowCount;
        table.RowCount++;
        table.Controls.Add(new Label
        {
            Text = label,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Margin = new Padding(3, 8, 8, 3),
        }, 0, row);
        table.Controls.Add(input, 1, row);
        input.Margin = new Padding(3, 4, 3, 4);
        if (trailing is not null)
        {
            table.Controls.Add(trailing, 2, row);
            trailing.Margin = new Padding(3, 3, 3, 3);
        }
    }

    private static void AddSeparator(TableLayoutPanel table)
    {
        var row = table.RowCount;
        table.RowCount++;
        var line = new Panel { Height = 1, Dock = DockStyle.Fill, BackColor = SystemColors.ControlDark, Margin = new Padding(3, 6, 3, 6) };
        table.Controls.Add(line, 0, row);
        table.SetColumnSpan(line, 3);
    }

    private static Button BrowseButton(EventHandler onClick)
    {
        var button = new Button { Text = "Gözat…", AutoSize = true };
        button.Click += onClick;
        return button;
    }

    private Control RequestButtons()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty };
        panel.Controls.Add(BrowseButton(BrowseRequest));
        var latest = new Button { Text = "En yeni", AutoSize = true };
        latest.Click += (_, _) => FillLatestRequest();
        panel.Controls.Add(latest);
        return panel;
    }

    private void WireEvents()
    {
        _generate.Click += (_, _) => Generate();
        _openFolder.Click += (_, _) => OpenOutputFolder();

        _requestPath.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        _requestPath.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                _requestPath.Text = files[0];
        };

        FormClosing += (_, _) => PersistSettings();
    }

    private void ApplySettings()
    {
        _keyPath.Text = _settings.KeyPath ?? DefaultKeyPath();
        _requestPath.Text = _settings.RequestPath ?? string.Empty;
        _customerId.Text = _settings.CustomerId ?? "ACME";
        _customerName.Text = _settings.CustomerName ?? "ACME Sanayi A.S.";
        _edition.Text = _settings.Edition ?? "enterprise";
        _features.Text = _settings.Features ?? "agent,sap";
        _maxAgents.Value = Math.Clamp(_settings.MaxAgents, (int)_maxAgents.Minimum, (int)_maxAgents.Maximum);
        _licenseId.Text = "LIC-001";

        var outputDirectory = _settings.OutputDirectory ?? DefaultOutputDirectory();
        _outputPath.Text = Path.Combine(outputDirectory, SuggestedFileName());

        if (string.IsNullOrWhiteSpace(_requestPath.Text))
            FillLatestRequest(silent: true);
    }

    private void PersistSettings()
    {
        _settings.KeyPath = _keyPath.Text;
        _settings.RequestPath = _requestPath.Text;
        _settings.OutputDirectory = SafeDirectoryOf(_outputPath.Text);
        _settings.CustomerId = _customerId.Text;
        _settings.CustomerName = _customerName.Text;
        _settings.Edition = _edition.Text;
        _settings.Features = _features.Text;
        _settings.MaxAgents = (int)_maxAgents.Value;
        _settings.Save();
    }

    // --- gozat / talep yardimcilari -------------------------------------------------------

    private void BrowseKey(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Satıcı özel anahtarı (şifreli PEM)",
            Filter = "PEM anahtarı (*.pem)|*.pem|Tüm dosyalar (*.*)|*.*",
        };
        if (File.Exists(_keyPath.Text)) dialog.InitialDirectory = Path.GetDirectoryName(_keyPath.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK) _keyPath.Text = dialog.FileName;
    }

    private void BrowseRequest(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Kurulum talebi (Studio'dan indirilen JSON)",
            Filter = "Kurulum talebi (*.json)|*.json|Tüm dosyalar (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _requestPath.Text = dialog.FileName;
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Üretilecek lisans dosyası",
            Filter = "Lisans dosyası (*.lic)|*.lic|Tüm dosyalar (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(_outputPath.Text) ? SuggestedFileName() : Path.GetFileName(_outputPath.Text),
        };
        var directory = SafeDirectoryOf(_outputPath.Text);
        if (directory is not null) dialog.InitialDirectory = directory;
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputPath.Text = dialog.FileName;
    }

    private void FillLatestRequest(bool silent = false)
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads))
        {
            if (!silent) Log("Downloads klasörü bulunamadı; kurulum talebini elle seçin.");
            return;
        }

        var latest = Directory.EnumerateFiles(downloads, "installation-request-*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            if (!silent) Log("Downloads içinde installation-request-*.json bulunamadı.");
            return;
        }

        _requestPath.Text = latest.FullName;
        Log($"En yeni kurulum talebi seçildi: {latest.Name}");
    }

    // --- uretim ---------------------------------------------------------------------------

    private void Generate()
    {
        if (!Validate(out var output)) return;

        _generate.Enabled = false;
        _openFolder.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            // Parola yalnizca bu surece, bir an icin. finally'de mutlaka temizlenir.
            Environment.SetEnvironmentVariable(PasswordEnvVar, _password.Text);

            var features = _features.Text.Trim();
            var args = new System.Collections.Generic.List<string>
            {
                "generate",
                "--request", _requestPath.Text.Trim(),
                "--output", output,
                "--key", _keyPath.Text.Trim(),
                "--key-password-env", PasswordEnvVar,
                "--license-id", _licenseId.Text.Trim(),
                "--customer-id", _customerId.Text.Trim(),
                "--customer-name", _customerName.Text.Trim(),
                "--edition", _edition.Text.Trim(),
                "--max-agents", ((int)_maxAgents.Value).ToString(),
                "--revision", ((int)_revision.Value).ToString(),
                "--expires", _expires.Value.ToString("yyyy-MM-dd"),
            };
            if (!string.IsNullOrEmpty(features))
            {
                args.Add("--features");
                args.Add(features);
            }

            var options = LicenseGenerationOptions.Parse([.. args]);
            var document = LicenseGenerationService.Generate(options);

            _lastOutput = output;
            _openFolder.Enabled = true;
            var payload = document.Payload;
            Log("");
            Log("BASARILI — Lisans üretildi.");
            Log($"    dosya       : {output}");
            Log($"    licenseId   : {payload.LicenseId} (revision {payload.Revision})");
            Log($"    müşteri     : {payload.CustomerName} ({payload.CustomerId})");
            Log($"    sürüm       : {payload.Edition}");
            Log($"    kurulum     : {payload.InstallationId}");
            Log($"    maxAgents   : {payload.MaxActivatedAgents}");
            Log($"    bitiş       : {payload.ExpiresAt:yyyy-MM-dd}");
            Log("");
            Log("Sıradaki adım: Studio → Lisanslama → \"İçe aktar\" ile bu .lic dosyasını yükleyin.");
            PersistSettings();
        }
        catch (LicenseGenerationException ex)
        {
            // Sir icermeyen, operatore gosterilebilir hata (uretici bilerek boyle tasarlandi).
            Log("");
            Log("HATA — Lisans üretilemedi: " + ex.Message);
        }
        catch (Exception ex)
        {
            Log("");
            Log("HATA — Beklenmeyen: " + ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PasswordEnvVar, null);
            Cursor = Cursors.Default;
            _generate.Enabled = true;
        }
    }

    private bool Validate(out string output)
    {
        output = _outputPath.Text.Trim();

        if (!File.Exists(_keyPath.Text.Trim()))
            return Fail("Satıcı anahtar dosyası bulunamadı. Geçerli bir .pem seçin.");
        if (string.IsNullOrEmpty(_password.Text))
            return Fail("Anahtar parolası boş olamaz.");
        if (!File.Exists(_requestPath.Text.Trim()))
            return Fail("Kurulum talebi (.json) bulunamadı. Studio'dan indirip seçin ya da \"En yeni\"yi kullanın.");
        if (string.IsNullOrWhiteSpace(_licenseId.Text))
            return Fail("Lisans No zorunludur.");
        if (string.IsNullOrWhiteSpace(_customerId.Text))
            return Fail("Müşteri No zorunludur.");
        if (string.IsNullOrWhiteSpace(_customerName.Text))
            return Fail("Müşteri adı zorunludur.");
        if (string.IsNullOrWhiteSpace(_edition.Text))
            return Fail("Sürüm (edition) zorunludur.");
        if (string.IsNullOrWhiteSpace(output))
            return Fail("Çıktı dosyası (.lic) belirtin.");

        return true;
    }

    private bool Fail(string message)
    {
        Log("UYARI — " + message);
        MessageBox.Show(this, message, "Eksik bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void OpenOutputFolder()
    {
        if (_lastOutput is null || !File.Exists(_lastOutput)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastOutput}\"") { UseShellExecute = true });
    }

    // --- kucuk yardimcilar ----------------------------------------------------------------

    private void Log(string message)
    {
        var line = message.Length == 0 ? Environment.NewLine : $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _log.AppendText(line);
    }

    private string SuggestedFileName()
    {
        var id = string.IsNullOrWhiteSpace(_customerId.Text) ? "musteri" : _customerId.Text.Trim();
        var safe = string.Concat(id.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        return (string.IsNullOrEmpty(safe) ? "musteri" : safe) + ".lic";
    }

    private static string DefaultKeyPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "rpa-vendor-keys", "vendor-private-key.pem");

    private static string DefaultOutputDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "rpa-vendor-keys");

    private static string? SafeDirectoryOf(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            return string.IsNullOrWhiteSpace(directory) ? null : directory;
        }
        catch
        {
            return null;
        }
    }
}
