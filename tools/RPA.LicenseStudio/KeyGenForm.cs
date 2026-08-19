namespace RPA.LicenseStudio;

using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

/// <summary>
/// Satici (vendor) imzalama anahtar cifti uretme penceresi — openssl/terminal GEREKMEZ,
/// tamamen .NET kriptosu. Sifreli PKCS#8 ozel anahtar (imzalamada kullanilir) + eslesen
/// acik anahtar (urunde Licensing:VendorPublicKeyPem olarak yapilandirilir) uretir.
///
/// GUVENLIK: parolayi OPERATOR girer (iki kez, dogrulanir); uygulama parolayi bellek disina
/// yazmaz, loglamaz, hicbir yere kalicilastirmaz. Parola yalnizca ozel anahtari sifrelemek icin
/// kullanilir ve kaybedilirse anahtar bir daha acilamaz (kurtarma yoktur).
/// </summary>
public sealed class KeyGenForm : Form
{
    private const string PrivateFileName = "vendor-private-key.pem";
    private const string PublicFileName = "vendor-public-key.pem";

    private readonly TextBox _directory = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _passwordRepeat = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button _generate = new() { Text = "Üret", AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(2, 6, 2, 2) };
    private readonly TextBox _publicKey = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 8.5F),
    };
    private readonly Button _copyPublic = new() { Text = "Açık anahtarı kopyala", AutoSize = true, Enabled = false };
    private readonly Button _copyAppsettings = new() { Text = "appsettings satırını kopyala", AutoSize = true, Enabled = false };

    /// <summary>Uretim basariliysa yeni ozel anahtar dosyasinin yolu; aksi halde null.</summary>
    public string? GeneratedPrivateKeyPath { get; private set; }

    private string? _appsettingsLine;

    public KeyGenForm(string defaultDirectory)
    {
        Text = "Yeni Satıcı Anahtarı Üret";
        MinimumSize = new Size(640, 520);
        Size = new Size(680, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5F);
        Padding = new Padding(14);
        MinimizeBox = false;
        AutoScroll = true;

        BuildLayout();
        _directory.Text = defaultDirectory;
        SetStatus("Kayıt klasörü ve bir parola belirleyin. Parolayı kasanıza kaydedin — kaybolursa anahtar açılamaz.", warning: false);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // giris alanlari
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // uret butonu
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));   // durum (sabit yukseklik → gorunur)
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // sonuc (acik anahtar)

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var browse = new Button { Text = "Gözat…", AutoSize = true };
        browse.Click += BrowseDirectory;
        AddRow(form, "Kayıt klasörü", _directory, browse);
        AddRow(form, "Parola", _password, null);
        AddRow(form, "Parola (tekrar)", _passwordRepeat, null);

        _generate.Click += OnGenerate;
        var generatePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 4, 0, 4) };
        generatePanel.Controls.Add(_generate);

        var statusPanel = new Panel { Dock = DockStyle.Fill };
        statusPanel.Controls.Add(_status);

        var resultPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        resultPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resultPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultPanel.Controls.Add(new Label
        {
            Text = "Açık anahtar — ürünün Licensing:VendorPublicKeyPem ayarına konur (sır değildir):",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 2),
        }, 0, 0);
        resultPanel.Controls.Add(_publicKey, 0, 1);
        var copyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        _copyPublic.Click += (_, _) => CopyToClipboard(_publicKey.Text, "Açık anahtar");
        _copyAppsettings.Click += (_, _) => CopyToClipboard(_appsettingsLine, "appsettings satırı");
        copyPanel.Controls.Add(_copyPublic);
        copyPanel.Controls.Add(_copyAppsettings);
        resultPanel.Controls.Add(copyPanel, 0, 2);

        root.Controls.Add(form, 0, 0);
        root.Controls.Add(generatePanel, 0, 1);
        root.Controls.Add(statusPanel, 0, 2);
        root.Controls.Add(resultPanel, 0, 3);
        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control input, Control? trailing)
    {
        var row = table.RowCount;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowCount = row + 1;
        table.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true, Margin = new Padding(3, 9, 10, 3) }, 0, row);
        input.Margin = new Padding(3, 5, 3, 5);
        table.Controls.Add(input, 1, row);
        if (trailing is not null)
        {
            trailing.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(trailing, 2, row);
        }
    }

    private void BrowseDirectory(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Anahtarların kaydedileceği klasör" };
        if (Directory.Exists(_directory.Text)) dialog.SelectedPath = _directory.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) _directory.Text = dialog.SelectedPath;
    }

    private void OnGenerate(object? sender, EventArgs e)
    {
        var directory = _directory.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            Warn("Kayıt klasörü belirtin.");
            return;
        }

        if (_password.Text.Length < 8)
        {
            Warn("Parola en az 8 karakter olmalı. (Girdiğiniz: " + _password.Text.Length + " karakter)");
            return;
        }

        if (!string.Equals(_password.Text, _passwordRepeat.Text, StringComparison.Ordinal))
        {
            Warn("İki parola alanı birbiriyle eşleşmiyor. Aynı parolayı iki kutuya da girin.");
            return;
        }

        _generate.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            Directory.CreateDirectory(directory);
            var privatePath = Path.Combine(directory, PrivateFileName);
            var publicPath = Path.Combine(directory, PublicFileName);

            // Var olan (ornegin parolasi kayip) anahtarlari SILME — zaman damgasiyla yedekle.
            BackupIfExists(privatePath);
            BackupIfExists(publicPath);

            using var rsa = RSA.Create(3072);

            // Sifreli PKCS#8: PrivateKeyLoader.ImportFromEncryptedPem'in bekledigi bicim.
            var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);
            var privatePem = new string(PemEncoding.Write("ENCRYPTED PRIVATE KEY",
                rsa.ExportEncryptedPkcs8PrivateKey(_password.Text.AsSpan(), pbe)));
            var publicPem = new string(PemEncoding.Write("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));

            File.WriteAllText(privatePath, privatePem);
            File.WriteAllText(publicPath, publicPem);

            GeneratedPrivateKeyPath = privatePath;
            _publicKey.Text = publicPem;
            _appsettingsLine = "\"VendorPublicKeyPem\": \"" + publicPem.Replace("\r\n", "\n").Replace("\n", "\\n") + "\"";
            _copyPublic.Enabled = true;
            _copyAppsettings.Enabled = true;

            SetStatus("Üretildi. Özel anahtar: " + privatePath, warning: false, success: true);
            _generate.Text = "Üretildi ✓";
            AcceptButton = null;

            var written = File.Exists(privatePath) && File.Exists(publicPath);
            var message =
                (written ? "Anahtar çifti üretildi.\n\n" : "Üretim tamamlandı ama dosya doğrulanamadı!\n\n")
                + "Özel anahtar (imzalama):\n" + privatePath + "\n\n"
                + "Açık anahtar (ürün ayarına konur):\n" + publicPath + "\n\n"
                + "Sıradaki adım: penceredeki \"Açık anahtarı kopyala\" ile açık anahtarı alıp ürünün "
                + "Licensing:VendorPublicKeyPem ayarına koyun. Bu pencereyi kapatınca özel anahtar ana ekranda seçili gelir.\n\n"
                + "Kayıt klasörünü açmak ister misiniz?";
            if (MessageBox.Show(this, message, "Anahtar üretildi", MessageBoxButtons.YesNo,
                    written ? MessageBoxIcon.Information : MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                OpenFolder(directory);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Üretilemedi: " + ex.Message, warning: true);
            _generate.Enabled = true;
            MessageBox.Show(this, "Anahtar üretilemedi:\n\n" + ex.Message, "Hata",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Warn(string message)
    {
        SetStatus(message, warning: true);
        MessageBox.Show(this, message, "Eksik/geçersiz bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void OpenFolder(string directory)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + directory + "\"") { UseShellExecute = true });
        }
        catch
        {
            // Klasor acilamazsa onemli degil — yol zaten mesajda gosterildi.
        }
    }

    private static void BackupIfExists(string path)
    {
        if (!File.Exists(path)) return;
        var backup = path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".old";
        File.Move(path, backup, overwrite: false);
    }

    private void CopyToClipboard(string? text, string label)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            SetStatus(label + " panoya kopyalandı.", warning: false, success: true);
        }
        catch (Exception ex)
        {
            SetStatus("Panoya kopyalanamadı: " + ex.Message, warning: true);
        }
    }

    private void SetStatus(string message, bool warning, bool success = false)
    {
        _status.Text = message;
        _status.ForeColor = success ? Color.ForestGreen : warning ? Color.Firebrick : SystemColors.ControlText;
    }
}
