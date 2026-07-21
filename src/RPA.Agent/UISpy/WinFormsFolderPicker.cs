namespace RPA.Agent.UISpy;

using System.Runtime.Versioning;
using System.Windows.Forms;

/// <summary>
/// Agent makinesinde native klasör seçim diyaloğu (<see cref="FolderBrowserDialog"/>) açan klasör
/// picker'ı. Studio'daki 🎯 düğmesi <c>kind:"folder"</c> ile bunu tetikler; kullanıcı ajanın gerçek
/// dosya sisteminde bir klasör seçer ve seçilen tam yol File.List gibi aktivitelerin <c>folder</c>
/// alanına yazılır. İptal (Cancel/kapatma) → null. Diyalog STA thread gerektirdiğinden ayrı bir
/// STA thread üzerinde çalıştırılır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinFormsFolderPicker : IFolderPicker
{
    public Task<string?> DetectOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? selected = null;
        var thread = new Thread(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "İş akışının tarayacağı klasörü seçin",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };

            if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                selected = dialog.SelectedPath;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // İptal (StopSpy/timeout) gelirse diyalogu bekleyen thread'i bloke etmeden çık; kullanıcı
        // diyalogu daha sonra kapatınca STA thread kendiliğinden sonlanır.
        while (!thread.Join(100))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult<string?>(null);
            }
        }

        return Task.FromResult(selected);
    }
}
