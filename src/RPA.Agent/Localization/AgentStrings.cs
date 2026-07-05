namespace RPA.Agent.Localization;

/// <summary>
/// Tray/Job List/UserPrompt pencereleri için basit i18n sözlüğü (Türkçe + İngilizce, Spec Bölüm 8).
/// Kaynak dosyası (.resx) yerine kod içi sözlük kullanılır — derleme/paketleme basitliği için;
/// anahtarlar aynı sözleşmeyi (key) korur, ileride .resx'e taşınabilir.
/// </summary>
public static class AgentStrings
{
    private static readonly Dictionary<string, (string Tr, string En)> Entries = new()
    {
        ["Tray.PauseJob"] = ("Duraklat", "Pause"),
        ["Tray.ResumeJob"] = ("Devam Et", "Resume"),
        ["Tray.StopJob"] = ("İşi Durdur", "Stop Job"),
        ["Tray.OpenJobList"] = ("İş Listesini Aç", "Open Job List"),
        ["Tray.Settings"] = ("Ayarlar", "Settings"),
        ["Tray.ExitAgent"] = ("Ajanı Kapat", "Exit Agent"),
        ["Tray.Offline"] = ("Çevrimdışı", "Offline"),
        ["Tray.Online"] = ("Çevrimiçi", "Online"),
        ["Tray.Reconnecting"] = ("Yeniden bağlanılıyor…", "Reconnecting…"),
        ["JobList.Title"] = ("İş Listesi", "Job List"),
        ["JobList.ColumnJobId"] = ("İş No", "Job ID"),
        ["JobList.ColumnWorkflow"] = ("Workflow", "Workflow"),
        ["JobList.ColumnStartedAt"] = ("Başlangıç", "Started"),
        ["JobList.ColumnCurrentStep"] = ("Geçerli Adım", "Current Step"),
        ["JobList.ColumnStatus"] = ("Durum", "Status"),
        ["UserPrompt.Title"] = ("Kullanıcı Girdisi Gerekli", "User Input Required"),
        ["UserPrompt.Submit"] = ("Gönder", "Submit"),
        ["UserPrompt.Cancel"] = ("İptal", "Cancel"),
        ["UserPrompt.TimedOut"] = ("Süre doldu", "Timed out"),
    };

    /// <summary>Verilen anahtar için dile göre metni döndürür. Bilinmeyen anahtar için anahtarın kendisini döndürür.</summary>
    public static string Get(string key, AgentLanguage language)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Entries.TryGetValue(key, out var value))
            return key;

        return language == AgentLanguage.Turkish ? value.Tr : value.En;
    }
}
