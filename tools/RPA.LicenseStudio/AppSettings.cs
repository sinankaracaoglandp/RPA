namespace RPA.LicenseStudio;

using System;
using System.IO;
using System.Text.Json;

/// <summary>
/// Yalnizca SIR ICERMEYEN kullanim kolayliklarini hatirlar (anahtar dosyasi yolu, musteri
/// varsayilanlari, cikti klasoru). Parola ASLA yazilmaz — ne bu dosyaya ne baska bir yere.
/// %APPDATA%\RPA.LicenseStudio\settings.json altinda tutulur.
/// </summary>
public sealed class AppSettings
{
    public string? KeyPath { get; set; }
    public string? RequestPath { get; set; }
    public string? OutputDirectory { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? Edition { get; set; }
    public string? Features { get; set; }
    public int MaxAgents { get; set; } = 3;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RPA.LicenseStudio",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Bozuk/erisilemeyen ayar dosyasi kritik degildir — varsayilanlarla devam.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch
        {
            // Ayar kaydedilemezse sessizce gec — islevsellik etkilenmez.
        }
    }
}
