namespace RPA.Infrastructure.Workflow;

using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// Aktivite kataloğunun tek doğruluk kaynağı (spec Bölüm 5.3 listesi).
/// Tüm MVP aktiviteleri burada fluent builder ile kaydedilir; hardcode dictionary yerine
/// registry pattern kullanılır — böylece yeni aktivite eklemek tek satırlık ekleme olur.
/// Kategoriler: Mantık, SAP GUI, SAP NCo, Web, API, Excel, CSV, E-posta, OTP, Dosya.
/// </summary>
public static class ActivityRegistry
{
    // Kategori sabitleri (Toolbox gruplaması için).
    public const string CatLogic = "Mantık";
    public const string CatSapGui = "SAP GUI";
    public const string CatSapNco = "SAP NCo";
    public const string CatWeb = "Web";
    public const string CatApi = "API";
    public const string CatExcel = "Excel";
    public const string CatCsv = "CSV";
    public const string CatEmail = "E-posta";
    public const string CatOtp = "OTP";
    public const string CatFile = "Dosya";
    public const string CatDesktop = "Masaüstü";
    public const string CatCode = "Kod & Veri";
    public const string CatVision = "Görüntü";
    public const string CatEInvoice = "E-Fatura";

    /// <summary>Tüm MVP aktivitelerini kaydeder ve immutable katalog döndürür.</summary>
    public static IReadOnlyDictionary<string, ActivityMetadata> BuildCatalog()
    {
        var b = new ActivityCatalogBuilder();

        RegisterLogic(b);
        RegisterSapGui(b);
        RegisterSapNco(b);
        RegisterWeb(b);
        RegisterApi(b);
        RegisterExcelCsv(b);
        RegisterEmail(b);
        RegisterOtp(b);
        RegisterFile(b);
        RegisterDesktop(b);
        RegisterCode(b);
        RegisterVision(b);
        RegisterEInvoice(b);

        return b.Build();
    }

    private static void RegisterEInvoice(ActivityCatalogBuilder b)
    {
        b.Activity("EInvoice.ReadUbl").DisplayName("E-Fatura UBL Oku").Category(CatEInvoice)
            .Input("filePath", "Sensitive", required: false).Input("xmlContent", "Sensitive", required: false)
            .Input("mappings", "JSON", required: false, pickerKind: "einvoice-mapping")
            .Input("outputBindings", "JSON", required: false)
            .Output("invoice", "JSON").Output("lines", "List<JSON>").Output("customFields", "JSON");

        b.Activity("EInvoice.ReadUblBatch").DisplayName("E-Fatura UBL Toplu Oku").Category(CatEInvoice)
            .Input("filePaths", "Sensitive", required: false).Input("xmlContents", "Sensitive", required: false)
            .Input("errorMode", "string", required: false, defaultValue: "Continue", options: new[] { "Continue", "Stop" })
            .Input("mappings", "JSON", required: false, pickerKind: "einvoice-mapping")
            .Input("outputBindings", "JSON", required: false).Output("results", "JSON");

        b.Activity("EInvoice.ReadProfile").DisplayName("E-Fatura Profili Oku").Category(CatEInvoice)
            .Input("projectId", "string").Input("profileId", "string", pickerKind: "einvoice-profile")
            .Input("profileVersion", "int")
            .Input("sourceMode", "string", defaultValue: "XmlContent", options: new[] { "FilePath", "XmlContent" })
            .Input("filePath", "Sensitive", required: false).Input("xmlContent", "Sensitive", required: false)
            .Input("outputVariable", "string", required: false, defaultValue: "fatura")
            .Output("fatura", "JSON");

        b.Activity("EInvoice.ReadProfileBatch").DisplayName("E-Fatura Profili Toplu Oku").Category(CatEInvoice)
            .Input("projectId", "string").Input("profileId", "string", pickerKind: "einvoice-profile")
            .Input("profileVersion", "int")
            .Input("sourceMode", "string", defaultValue: "Folder", options: new[] { "Folder", "FilePaths", "XmlContents" })
            .Input("folderPath", "Sensitive", required: false)
            .Input("fileFilter", "string", required: false, defaultValue: "*.xml")
            .Input("includeSubfolders", "bool", required: false, defaultValue: false)
            .Input("filePaths", "Sensitive", required: false).Input("xmlContents", "Sensitive", required: false)
            .Input("outputVariable", "string", required: false, defaultValue: "faturalar")
            .Output("faturalar", "JSON");
    }

    // ---- Mantık ----
    private static void RegisterLogic(ActivityCatalogBuilder b)
    {
        b.Activity("Logic.Assign").DisplayName("Değişkene Ata").Category(CatLogic)
            .Description("Bir değişkene ifade veya literal değer atar.")
            .Input("variableName", "string").Input("value", "JSON");

        b.Activity("Logic.If").DisplayName("Eğer / Koşul").Category(CatLogic)
            .Description("Boolean koşula göre dallanır (true/false portları).")
            .Input("condition", "string");

        b.Activity("Logic.ForEach").DisplayName("Her Biri İçin").Category(CatLogic)
            .Description("Bir dizi/DataTable üzerinde döner.")
            .Input("items", "string").Input("itemVariable", "string");

        b.Activity("Logic.For").DisplayName("Sayaç Döngüsü").Category(CatLogic)
            .Description("Başlangıç ve dahil bitiş değeri arasında sayaçla döner.")
            .Input("start", "int").Input("end", "int")
            .Input("step", "int", required: false, defaultValue: 1)
            .Input("indexVariable", "string");

        b.Activity("Logic.While").DisplayName("İken Döngü").Category(CatLogic)
            .Description("Koşul doğru olduğu sürece body'yi tekrarlar.")
            .Input("condition", "string");

        b.Activity("Logic.TryCatch").DisplayName("Dene / Yakala").Category(CatLogic)
            .Description("Try bloğundaki hatayı yakalar; catch/finally akışı.")
            .Input("exceptionVariable", "string", required: false);

        b.Activity("Logic.Delay").DisplayName("Bekle").Category(CatLogic)
            .Description("Belirtilen milisaniye kadar bekler.")
            .Input("durationMs", "int").DefaultProperty("durationMs", 1000);

        b.Activity("Logic.Log").DisplayName("Log Yaz").Category(CatLogic)
            .Description("Yapılandırılmış log kaydı yazar (korelasyon ID otomatik).")
            .Input("message", "string").Input("level", "string", required: false, defaultValue: "Information");

        b.Activity("Logic.Checkpoint").DisplayName("Kontrol Noktası").Category(CatLogic)
            .Description("Idempotency/resume için ilerleme kontrol noktası.")
            .Input("referenceKey", "string");

        b.Activity("Logic.UserPrompt").DisplayName("Kullanıcıya Sor").Category(CatLogic)
            .Description("Attended: kullanıcıdan giriş ister (zaman aşımlı).")
            .Input("promptTitle", "string").Input("promptInputVariable", "string")
            .Input("promptTimeoutSeconds", "int", required: false, defaultValue: 180)
            .Output("input", "string")
            .ExceptionClassification("Timeout", ExceptionType.Business);

        b.Activity("Logic.Terminate").DisplayName("Sonlandır").Category(CatLogic)
            .Description("Workflow'u Business/System exception ile sonlandırır.")
            .Input("message", "string").Input("exceptionType", "string", required: false, defaultValue: "System");
    }

    // ---- SAP GUI Scripting ----
    private static void RegisterSapGui(ActivityCatalogBuilder b)
    {
        const string cap = "sap-gui";
        b.Activity("Sap.Gui.Connect").DisplayName("SAP GUI Bağlan").Category(CatSapGui).Capability(cap)
            .Description("SAP GUI Scripting oturumuna bağlanır.")
            .Input("systemId", "string", required: true, description: "SAP System ID (örn. DEV) — SAP Logon sistem girişi")
            .Input("client", "string", required: true, description: "SAP Client (örn. 100)")
            .Input("userId", "string", required: true, description: "SAP kullanıcı adı")
            .Input("credentialName", "Credential", required: true, description: "Şifre için Vault key referansı")
            .Input("language", "string", required: false, defaultValue: "EN", description: "Oturum dili (örn. TR, EN)")
            .Output("session", "JSON");

        b.Activity("Sap.Gui.Login").DisplayName("SAP GUI Giriş").Category(CatSapGui).Capability(cap)
            .Description("Client/kullanıcı ile SAP oturumu açar (credential Vault'tan).")
            .Input("client", "string").Input("credentialName", "Credential")
            .ExceptionClassification("MessageType=='E'", ExceptionType.Business);

        b.Activity("Sap.Gui.ExecuteTransaction").DisplayName("İşlem Kodu Çalıştır").Category(CatSapGui).Capability(cap)
            .Description("Transaction kodu çalıştırır (örn. MM01).")
            .Input("transactionCode", "string");

        b.Activity("Sap.Gui.Click").DisplayName("SAP GUI Tıkla").Category(CatSapGui).Capability(cap)
            .Description("Element ID'ye tıklar (wnd[0]/usr/...).")
            .Input("elementId", "string", pickerKind: "sap");

        b.Activity("Sap.Gui.SetText").DisplayName("SAP GUI Metin Yaz").Category(CatSapGui).Capability(cap)
            .Description("Bir alana metin yazar.")
            .Input("elementId", "string", pickerKind: "sap").Input("text", "string");

        b.Activity("Sap.Gui.GetText").DisplayName("SAP GUI Metin Oku").Category(CatSapGui).Capability(cap)
            .Description("Bir alandan metin okur.")
            .Input("elementId", "string", pickerKind: "sap").Output("text", "string");

        b.Activity("Sap.Gui.SelectTab").DisplayName("SAP GUI Sekme Seç").Category(CatSapGui).Capability(cap)
            .Description("Sekme kontrolünde sekme seçer.")
            .Input("elementId", "string", pickerKind: "sap");

        b.Activity("Sap.Gui.SelectMenu").DisplayName("SAP GUI Menü Seç").Category(CatSapGui).Capability(cap)
            .Description("Menü çubuğunda metin yoluyla gezinip menü öğesi seçer (Sistem/Liste/Yazdır).")
            .Input("menuPath", "string", description: "'/' ile ayrılmış menü metni yolu.");

        b.Activity("Sap.Gui.GridRead").DisplayName("SAP GUI ALV Oku").Category(CatSapGui).Capability(cap)
            .Description("ALV grid içeriğini DataTable olarak okur.")
            .Input("gridElementId", "string", pickerKind: "sap").Output("data", "DataTable");

        b.Activity("Sap.Gui.Screenshot").DisplayName("SAP GUI Ekran Görüntüsü").Category(CatSapGui).Capability(cap)
            .Description("Aktif SAP penceresinin görüntüsünü alır.")
            .Output("path", "string");
    }

    // ---- SAP NCo ----
    private static void RegisterSapNco(ActivityCatalogBuilder b)
    {
        const string cap = "sap-nco";
        b.Activity("Sap.Nco.CallBapi").DisplayName("SAP BAPI Çağır").Category(CatSapNco).Capability(cap)
            .Description("NCo ile BAPI çağırır; parametre ve tablo eşleme.")
            .Input("bapiName", "string")
            .Input("inputs", "JSON", required: false)
            .Input("tableInputs", "JSON", required: false)
            .Output("result", "JSON")
            .ExceptionClassification("ReturnType=='E'", ExceptionType.Business);

        b.Activity("Sap.Nco.CallRfc").DisplayName("SAP RFC Çağır").Category(CatSapNco).Capability(cap)
            .Description("Genel RFC fonksiyon modülü çağırır.")
            .Input("rfcName", "string").Input("inputs", "JSON", required: false)
            .Input("tableInputs", "JSON", required: false).Output("result", "JSON")
            .ExceptionClassification("ReturnType=='E'", ExceptionType.Business);

        b.Activity("Sap.Nco.ReadTable").DisplayName("SAP Tablo Oku").Category(CatSapNco).Capability(cap)
            .Description("RFC_READ_TABLE ile tablo okur.")
            .Input("tableName", "string").Input("fields", "JSON", required: false)
            .Input("whereClause", "string", required: false).Output("data", "DataTable");

        b.Activity("Sap.Nco.Commit").DisplayName("SAP Commit").Category(CatSapNco).Capability(cap)
            .Description("BAPI_TRANSACTION_COMMIT çağırır.")
            .Input("wait", "bool", required: false, defaultValue: true);

        b.Activity("Sap.Nco.Rollback").DisplayName("SAP Rollback").Category(CatSapNco).Capability(cap)
            .Description("BAPI_TRANSACTION_ROLLBACK çağırır.");
    }

    // ---- Web (Playwright) ----
    private static void RegisterWeb(ActivityCatalogBuilder b)
    {
        const string cap = "web";
        b.Activity("Web.Open").DisplayName("Tarayıcı Aç").Category(CatWeb).Capability(cap)
            .Description("Yeni tarayıcı oturumu açar.")
            .Input("browser", "string", required: false, defaultValue: "chromium",
                description: "Tarayıcı türü", options: new[] { "chromium", "chrome", "edge" })
            .Input("headless", "bool", required: false, defaultValue: false,
                description: "Tarayıcıyı görünmez modda çalıştır").Output("session", "JSON");

        b.Activity("Web.Goto").DisplayName("Adrese Git").Category(CatWeb).Capability(cap)
            .Description("Verilen URL'ye gider.").Input("url", "string");

        b.Activity("Web.Click").DisplayName("Web Tıkla").Category(CatWeb).Capability(cap)
            .Description("Selector ile elemente tiklar veya hover yapar; acilir menu icin sonraki selector'u bekleyebilir.")
            .Input("selector", "string", pickerKind: "web")
            .Input("action", "string", required: false, defaultValue: "click")
            .Input("waitSelector", "string", required: false)
            .Input("timeoutMs", "int", required: false, defaultValue: 30000)
            .Input("steps", "JSON", required: false);

        b.Activity("Web.Fill").DisplayName("Web Alan Doldur").Category(CatWeb).Capability(cap)
            .Description("Bir input alanını doldurur.")
            .Input("selector", "string", pickerKind: "web").Input("value", "string");

        b.Activity("Web.GetText").DisplayName("Web Metin Oku").Category(CatWeb).Capability(cap)
            .Description("Elementin metnini okur.")
            .Input("selector", "string", pickerKind: "web")
            .Input("outputVariable", "string", required: false)
            .Output("text", "string");

        b.Activity("Web.WaitFor").DisplayName("Web Bekle").Category(CatWeb).Capability(cap)
            .Description("Bir elementin görünmesini/durumunu bekler.")
            .Input("selector", "string", pickerKind: "web").Input("timeoutMs", "int", required: false, defaultValue: 30000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Web.Download").DisplayName("Web İndir").Category(CatWeb).Capability(cap)
            .Description("Tetiklenen indirmeyi kaydeder.")
            .Input("selector", "string", pickerKind: "web").Input("targetPath", "string").Output("path", "string");

        b.Activity("Web.Upload").DisplayName("Web Yükle").Category(CatWeb).Capability(cap)
            .Description("Dosya input'una dosya yükler.")
            .Input("selector", "string", pickerKind: "web").Input("filePath", "string");

        b.Activity("Web.Screenshot").DisplayName("Web Ekran Görüntüsü").Category(CatWeb).Capability(cap)
            .Description("Sayfa/element görüntüsü alır.")
            .Input("selector", "string", required: false, pickerKind: "web")
            .Input("path", "string")
            .Output("path", "string");

        b.Activity("Web.FrameSwitch").DisplayName("Frame/Sekme Geç").Category(CatWeb).Capability(cap)
            .Description("iframe veya sekme bağlamına geçer.")
            .Input("frameSelector", "string", required: false).Input("tabIndex", "int", required: false);
    }

    // ---- API ----
    private static void RegisterApi(ActivityCatalogBuilder b)
    {
        b.Activity("Api.HttpRequest").DisplayName("HTTP İsteği").Category(CatApi).Capability("api")
            .Description("HTTP isteği (Basic/Bearer/API-key şablonları); JSON parse/build.")
            .Input("method", "string", defaultValue: "GET")
            .Input("url", "string")
            .Input("headers", "JSON", required: false)
            .Input("body", "JSON", required: false)
            .Input("authType", "string", required: false)
            .Input("credentialName", "Credential", required: false)
            .Output("statusCode", "int").Output("responseBody", "JSON")
            .ExceptionClassification("StatusCode>=500", ExceptionType.System);
    }

    // ---- Excel / CSV ----
    private static void RegisterExcelCsv(ActivityCatalogBuilder b)
    {
        b.Activity("Excel.Read").DisplayName("Excel Oku").Category(CatExcel).Capability("excel")
            .Description("Aralık/tablo okur → DataTable.")
            .Input("filePath", "string").Input("sheet", "string", required: false)
            .Input("range", "string", required: false).Output("data", "DataTable");

        b.Activity("Excel.Write").DisplayName("Excel Yaz").Category(CatExcel).Capability("excel")
            .Description("DataTable'ı çalışma sayfasına yazar.")
            .Input("filePath", "string").Input("sheet", "string", required: false)
            .Input("data", "DataTable").Input("startCell", "string", required: false, defaultValue: "A1");

        b.Activity("Excel.SheetManage").DisplayName("Excel Sayfa Yönet").Category(CatExcel).Capability("excel")
            .Description("Sayfa ekle/sil/yeniden adlandır.")
            .Input("filePath", "string").Input("operation", "string").Input("sheet", "string");

        b.Activity("Excel.Close").DisplayName("Excel Kapat").Category(CatExcel).Capability("excel")
            .Description("Açık çalışma kitabını kaydeder/kapatır.")
            .Input("filePath", "string").Input("save", "bool", required: false, defaultValue: true);

        b.Activity("Csv.Read").DisplayName("CSV Oku").Category(CatCsv).Capability("csv")
            .Description("CSV dosyasını DataTable olarak okur.")
            .Input("filePath", "string").Input("delimiter", "string", required: false, defaultValue: ",")
            .Output("data", "DataTable");

        b.Activity("Csv.Write").DisplayName("CSV Yaz").Category(CatCsv).Capability("csv")
            .Description("DataTable'ı CSV'ye yazar.")
            .Input("filePath", "string").Input("data", "DataTable")
            .Input("delimiter", "string", required: false, defaultValue: ",");
    }

    // ---- E-posta ----
    private static void RegisterEmail(ActivityCatalogBuilder b)
    {
        const string cap = "email";
        b.Activity("Email.Send").DisplayName("E-posta Gönder").Category(CatEmail).Capability(cap)
            .Description("SMTP/EWS ile e-posta gönderir.")
            .Input("to", "string").Input("subject", "string").Input("body", "string")
            .Input("attachments", "JSON", required: false).Input("credentialName", "Credential");

        b.Activity("Email.ReadInbox").DisplayName("Gelen Kutusu Oku").Category(CatEmail).Capability(cap)
            .Description("IMAP/EWS ile gelen kutusunu okur (filtreli).")
            .Input("folder", "string", required: false, defaultValue: "INBOX")
            .Input("filter", "JSON", required: false).Input("credentialName", "Credential")
            .Output("messages", "JSON");

        b.Activity("Email.WatchInbox").DisplayName("Gelen Kutusu İzle").Category(CatEmail).Capability(cap)
            .Description("Yeni eşleşen e-postayı bekler (tetikleyici/OTP).")
            .Input("filter", "JSON").Input("timeoutSeconds", "int", required: false, defaultValue: 180)
            .Input("credentialName", "Credential").Output("message", "JSON")
            .ExceptionClassification("Timeout", ExceptionType.Business);

        b.Activity("Email.DownloadAttachment").DisplayName("Ek İndir").Category(CatEmail).Capability(cap)
            .Description("E-posta ekini diske indirir.")
            .Input("messageId", "string").Input("targetFolder", "string").Output("paths", "JSON");
    }

    // ---- OTP ----
    private static void RegisterOtp(ActivityCatalogBuilder b)
    {
        // 5 kanal: Email, TOTP, GsmModem, PhoneForward, HumanApproval (spec Bölüm 7).
        b.Activity("Otp.GetOtp").DisplayName("OTP Kodu Al").Category(CatOtp).Capability("otp")
            .Description("Kanal-bağımsız OTP/2FA kodu getirir (Email/TOTP/GSM/Yönlendirme/İnsan onaylı); fallback zinciri.")
            .Input("channel", "string", description: "Email | Totp | GsmModem | PhoneForward | HumanApproval")
            .Input("config", "JSON", required: false)
            .Input("timeoutSeconds", "int", required: false, defaultValue: 180)
            .Output("code", "string")
            .ExceptionClassification("Timeout", ExceptionType.Business);
    }

    // ---- Dosya ----
    private static void RegisterFile(ActivityCatalogBuilder b)
    {
        const string cap = "file";
        b.Activity("File.Copy").DisplayName("Dosya Kopyala").Category(CatFile).Capability(cap)
            .Description("Dosyayı hedefe kopyalar.")
            .Input("source", "string").Input("destination", "string")
            .Input("overwrite", "bool", required: false, defaultValue: false);

        b.Activity("File.Move").DisplayName("Dosya Taşı").Category(CatFile).Capability(cap)
            .Description("Dosyayı taşır/yeniden adlandırır.")
            .Input("source", "string").Input("destination", "string");

        b.Activity("File.Delete").DisplayName("Dosya Sil").Category(CatFile).Capability(cap)
            .Description("Dosyayı siler.").Input("path", "string");

        b.Activity("File.List").DisplayName("Dosya Listele").Category(CatFile).Capability(cap)
            .Description("Klasördeki dosyaları listeler (pattern).")
            .Input("folder", "string").Input("pattern", "string", required: false, defaultValue: "*")
            .Output("files", "JSON");

        b.Activity("File.Zip").DisplayName("Sıkıştır (Zip)").Category(CatFile).Capability(cap)
            .Description("Dosya/klasörü zip arşivine ekler.")
            .Input("source", "string").Input("zipPath", "string").Output("path", "string");

        b.Activity("File.Unzip").DisplayName("Aç (Unzip)").Category(CatFile).Capability(cap)
            .Description("Zip arşivini hedef klasöre açar.")
            .Input("zipPath", "string").Input("targetFolder", "string").Output("files", "JSON");
    }

    // ---- Windows Masaüstü (Desktop.*, UIA/FlaUI) — Spec Bölüm 5, Paket E ----
    private static void RegisterDesktop(ActivityCatalogBuilder b)
    {
        const string cap = "desktop";

        b.Activity("Desktop.Attach").DisplayName("Uygulamaya Bağlan").Category(CatDesktop).Capability(cap)
            .Description("Çalışan bir Windows uygulamasının penceresine bağlanır.")
            .Input("processName", "string", required: false, description: "Süreç adı (örn. notepad).")
            .Input("windowTitle", "string", required: false, description: "Pencere başlığı (regex).")
            .Output("windowHandle", "string");

        b.Activity("Desktop.Launch").DisplayName("Uygulama Başlat").Category(CatDesktop).Capability(cap)
            .Description("Bir uygulamayı başlatır ve penceresine bağlanır.")
            .Input("path", "string").Input("arguments", "string", required: false)
            .Input("waitForIdle", "bool", required: false, defaultValue: true)
            .Output("windowHandle", "string");

        b.Activity("Desktop.Click").DisplayName("Masaüstü Tıkla").Category(CatDesktop).Capability(cap)
            .Description("Bir elemente tıklar (buton, menü).")
            .Input("selector", "string", pickerKind: "desktop")
            .Input("clickType", "string", required: false, defaultValue: "left", options: new[] { "left", "right", "double" });

        b.Activity("Desktop.SetText").DisplayName("Masaüstü Metin Yaz").Category(CatDesktop).Capability(cap)
            .Description("Bir alana (textbox) metin yazar.")
            .Input("selector", "string", pickerKind: "desktop").Input("text", "string", required: false);

        b.Activity("Desktop.GetText").DisplayName("Masaüstü Metin Oku").Category(CatDesktop).Capability(cap)
            .Description("Bir elementin metnini okur.")
            .Input("selector", "string", pickerKind: "desktop").Output("text", "string");

        b.Activity("Desktop.SelectItem").DisplayName("Masaüstü Öğe Seç").Category(CatDesktop).Capability(cap)
            .Description("Combobox / liste / menüde bir öğe seçer.")
            .Input("selector", "string", pickerKind: "desktop").Input("item", "string");

        b.Activity("Desktop.SendKeys").DisplayName("Masaüstü Tuş Gönder").Category(CatDesktop).Capability(cap)
            .Description("Sıralı tuş vuruşları (Ctrl+A, F4, Home…) ve/veya düz metin gönderir. Selector boşsa aktif odağa.")
            .Input("selector", "string", required: false, pickerKind: "desktop").Input("keys", "string", pickerKind: "keystroke-sequence");

        b.Activity("Desktop.WaitFor").DisplayName("Masaüstü Bekle").Category(CatDesktop).Capability(cap)
            .Description("Bir element görünür/etkin olana kadar bekler (timeout → System).")
            .Input("selector", "string", pickerKind: "desktop")
            .Input("timeoutMs", "int", required: false, defaultValue: 10000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Desktop.Screenshot").DisplayName("Masaüstü Ekran Görüntüsü").Category(CatDesktop).Capability(cap)
            .Description("Ekran görüntüsü alır; selector doluysa yalnız o element.")
            .Input("selector", "string", required: false, pickerKind: "desktop").Output("path", "string");
    }

    // ---- Kod & Veri ----
    private static void RegisterCode(ActivityCatalogBuilder b)
    {
        b.Activity("System.InvokeCode").DisplayName("C# Kod Çalıştır").Category(CatCode).Capability("code")
            .Description("Roslyn ile C# kodu çalıştırır. Get(\"ad\") oku, Set(\"ad\", deger) yaz, "
                       + "ToDataTable(rows)/ToRows(dt) ile DataTable işle. Güvenlik: sandbox'sız.")
            .Input("code", "string");

        b.Activity("Data.ToDataTable").DisplayName("DataTable'a Çevir").Category(CatCode)
            .Description("Satır listesini (sütun-değer) gerçek System.Data.DataTable'a dönüştürür.")
            .Input("rows", "JSON").Output("table", "DataTable");

        b.Activity("Data.FromDataTable").DisplayName("DataTable'dan Satırlar").Category(CatCode)
            .Description("DataTable'ı satır listesine (sütun-değer) dönüştürür.")
            .Input("table", "DataTable").Output("rows", "JSON");
    }

    // ---- Görüntü/OCR Fallback (Vision.*) — Paket F ----
    private static void RegisterVision(ActivityCatalogBuilder b)
    {
        const string cap = "vision";

        b.Activity("Vision.Click").DisplayName("Görüntüye Tıkla").Category(CatVision).Capability(cap)
            .Description("Ekranda bir görüntüyü bulur ve merkezine tıklar.")
            .Input("image", "string", pickerKind: "image", description: "Aranacak görüntü (base64 PNG).")
            .Input("confidence", "number", required: false, defaultValue: 0.8)
            .Input("clickType", "string", required: false, defaultValue: "left", options: new[] { "left", "right", "double" })
            .Input("timeoutMs", "int", required: false, defaultValue: 5000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.ClickSequence").DisplayName("Sıralı Görüntüye Tıkla (Menü)").Category(CatVision).Capability(cap)
            .Description("Tek node içinde sırayla birden çok görüntüye tıklar (iç içe menüler).")
            .Input("steps", "string", pickerKind: "image-sequence", description: "Adım listesi (JSON): {image, clickType, waitMs}.")
            .Input("confidence", "number", required: false, defaultValue: 0.8)
            .Input("timeoutMs", "int", required: false, defaultValue: 5000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.WaitFor").DisplayName("Görüntü Bekle").Category(CatVision).Capability(cap)
            .Description("Bir görüntü ekranda görünene kadar bekler (timeout → System).")
            .Input("image", "string", pickerKind: "image")
            .Input("confidence", "number", required: false, defaultValue: 0.8)
            .Input("timeoutMs", "int", required: false, defaultValue: 10000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.Exists").DisplayName("Görüntü Var mı?").Category(CatVision).Capability(cap)
            .Description("Görüntü ekranda var mı; 'exists' (bool) döner, fırlatmaz.")
            .Input("image", "string", pickerKind: "image")
            .Input("confidence", "number", required: false, defaultValue: 0.8)
            .Input("timeoutMs", "int", required: false, defaultValue: 0)
            .Output("exists", "bool");

        b.Activity("Vision.GetText").DisplayName("Görüntüden Metin Oku (OCR)").Category(CatVision).Capability(cap)
            .Description("Bir ekran bölgesinden (boşsa tam ekran) OCR ile metin okur.")
            .Input("region", "string", required: false, pickerKind: "image", description: "Bölge {x,y,width,height}.")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Output("text", "string");

        b.Activity("Vision.ClickText").DisplayName("Metne Tıkla (OCR)").Category(CatVision).Capability(cap)
            .Description("OCR ile bir metni bulur ve tıklar (timeout → System).")
            .Input("text", "string")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Input("matchMode", "string", required: false, defaultValue: "contains", options: new[] { "contains", "exact" })
            .Input("clickType", "string", required: false, defaultValue: "left", options: new[] { "left", "right", "double" })
            .Input("timeoutMs", "int", required: false, defaultValue: 5000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.ClickTextOffset").DisplayName("Çapaya Göre Tıkla (OCR)").Category(CatVision).Capability(cap)
            .Description("OCR metin çapasından piksel ofsetle tıklar (etiket-yanı boş alanlar).")
            .Input("anchor", "string", pickerKind: "text-offset", description: "Çapa + ofset (JSON): {anchorText, dx, dy}.")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Input("matchMode", "string", required: false, defaultValue: "contains", options: new[] { "contains", "exact" })
            .Input("clickType", "string", required: false, defaultValue: "left", options: new[] { "left", "right", "double" })
            .Input("timeoutMs", "int", required: false, defaultValue: 5000)
            .ExceptionClassification("Timeout", ExceptionType.System);

        b.Activity("Vision.TextExists").DisplayName("Metin Var mı? (OCR)").Category(CatVision).Capability(cap)
            .Description("Metin ekranda var mı; 'exists' (bool) döner, fırlatmaz.")
            .Input("text", "string")
            .Input("language", "string", required: false, defaultValue: "tur+eng")
            .Input("matchMode", "string", required: false, defaultValue: "contains", options: new[] { "contains", "exact" })
            .Input("timeoutMs", "int", required: false, defaultValue: 0)
            .Output("exists", "bool");
    }
}
