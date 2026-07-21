namespace RPA.Infrastructure.SAP;

using System.Runtime.Versioning;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Gerçek SAP GUI Scripting <c>GuiSession</c> COM nesnesini saran <see cref="ISapGuiSession"/>.
/// Tüm işlemler <c>findById</c> ile elementi bulup COM üyelerini çağırır ve oturumun STA
/// thread'inde (<see cref="SapStaThread"/>) marshallanır. Element bulunamama/COM hataları
/// <see cref="SystemException"/> olarak yükseltilir (Kontrat: SAP UI hataları System).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ComSapGuiSession : ISapGuiSession
{
    private readonly SapStaThread _sta;
    private readonly object _session; // GuiSession (COM)
    private bool _connected = true;

    public ComSapGuiSession(SapStaThread sta, object session, string id)
    {
        _sta = sta ?? throw new ArgumentNullException(nameof(sta));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Id = id;
    }

    public string Id { get; }

    public bool IsConnected => _connected;

    public Task StartTransactionAsync(string transactionCode) => Run(() =>
    {
        // Kanal "/nMM01" biçiminde iletir; GuiSession.StartTransaction düz tcode bekler.
        var code = transactionCode.Trim();
        if (code.StartsWith("/n", StringComparison.OrdinalIgnoreCase))
        {
            code = code[2..];
        }
        SapCom.Invoke(_session, "StartTransaction", code);
    });

    public Task SetTextAsync(string elementId, string text) => Run(() =>
    {
        var el = Find(elementId);
        SapCom.Set(el, "Text", text ?? string.Empty);
    });

    public Task<string> GetTextAsync(string elementId) => Run(() =>
    {
        var el = Find(elementId);
        return (string)(SapCom.Get(el, "Text") ?? string.Empty);
    });

    public Task ClickAsync(string elementId) => Run(() =>
    {
        var el = Find(elementId);
        // Buton → Press(); menü/diğer → Select(); ikisi de olmazsa hata yükselir.
        try { SapCom.Invoke(el, "Press"); }
        catch { SapCom.Invoke(el, "Select"); }
    });

    public Task SelectTabAsync(string elementId) => Run(() =>
    {
        var el = Find(elementId);
        SapCom.Invoke(el, "Select");
    });

    public Task SendVKeyAsync(int vKey, string windowId) => Run(() =>
    {
        // sendVKey PENCERE (GuiFrameWindow) uzerinde cagrilir, element uzerinde degil.
        var window = Find(windowId);
        SapCom.Invoke(window, "sendVKey", vKey);
    });

    public Task SelectMenuAsync(IReadOnlyList<string> menuTexts) => Run(() =>
    {
        if (menuTexts is null || menuTexts.Count == 0)
        {
            throw new SystemException("SAP GUI menü yolu boş olamaz.");
        }

        // Menü çubuğundan başla (wnd[0]/mbar) ve her segmentte metne göre alt menüye in.
        var current = Find("wnd[0]/mbar");
        object? target = null;
        for (var i = 0; i < menuTexts.Count; i++)
        {
            var children = SapCom.Get(current, "Children")
                ?? throw new SystemException($"SAP GUI menüsünde '{menuTexts[i]}' için alt öğe yok.");
            target = FindMenuChildByText(children, menuTexts[i]);
            current = target;
        }

        SapCom.Invoke(target!, "Select");
    });

    /// <summary>Bir GuiComponentCollection içinde Text'i verilen metinle eşleşen menü öğesini bulur.</summary>
    private static object FindMenuChildByText(object children, string text)
    {
        var count = Convert.ToInt32(SapCom.Get(children, "Count"));
        var wanted = NormalizeMenuText(text);

        // Önce tam (normalize) eşleşme, bulunamazsa StartsWith/Contains.
        object? contains = null;
        for (var i = 0; i < count; i++)
        {
            var child = SapCom.Invoke(children, "ElementAt", i);
            if (child is null)
            {
                continue;
            }
            string childText;
            try { childText = NormalizeMenuText((string?)SapCom.Get(child, "Text") ?? string.Empty); }
            catch { continue; } // ayraç/başlıksız öğe

            if (childText.Length == 0)
            {
                continue;
            }
            if (childText == wanted)
            {
                return child;
            }
            if (contains is null && (childText.StartsWith(wanted, StringComparison.Ordinal) || childText.Contains(wanted, StringComparison.Ordinal)))
            {
                contains = child;
            }
        }

        return contains
            ?? throw new SystemException($"SAP GUI menüsünde '{text}' öğesi bulunamadı.");
    }

    /// <summary>Menü metnini karşılaştırma için normalleştirir: kısayol '&' işaretini, sonundaki '...' ve boşlukları temizler, küçük harfe indirir.</summary>
    private static string NormalizeMenuText(string text)
        => text.Replace("&", string.Empty).TrimEnd('.', ' ', '\t').Trim().ToLowerInvariant();

    public Task<List<Dictionary<string, object?>>> ReadGridAsync(string gridId) => Run(() =>
    {
        var grid = Find(gridId);
        var rows = new List<Dictionary<string, object?>>();

        int rowCount = Convert.ToInt32(SapCom.Get(grid, "RowCount"));
        var columnOrder = SapCom.Get(grid, "ColumnOrder")
            ?? throw new SystemException($"SAP GUI grid kolon listesi alinamadi: '{gridId}'.");
        // SAP koleksiyonları eleman sayısını sürüme/tipe göre Count VEYA Length ile yayınlar;
        // yalnız "Count" okumak UI Spy tarafında sessiz başarısızlığa yol açmıştı (2026-07-21).
        var colCount = SapGuiAutomation.GetCollectionCount(columnOrder);

        var columns = new List<string>(colCount);
        for (var c = 0; c < colCount; c++)
        {
            columns.Add((string)(SapCom.Invoke(columnOrder, "ElementAt", c) ?? string.Empty));
        }

        for (var r = 0; r < rowCount; r++)
        {
            var row = new Dictionary<string, object?>(columns.Count);
            foreach (var col in columns)
            {
                try { row[col] = (string?)SapCom.Invoke(grid, "GetCellValue", r, col); }
                catch { row[col] = null; /* sanal/yüklenmemiş hücre */ }
            }
            rows.Add(row);
        }

        return rows;
    });

    public Task<byte[]> CaptureScreenAsync() => Run(() =>
    {
        var wnd = Find("wnd[0]");
        var tmp = Path.Combine(Path.GetTempPath(), $"sapshot_{Guid.NewGuid():N}.png");
        // HardCopy yazdığı dosyanın adını döndürür (SAP GUI çalışma dizinine düşebilir).
        var written = (string)(SapCom.Invoke(wnd, "HardCopy", tmp) ?? tmp);
        var path = File.Exists(written) ? written : tmp;
        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes;
        }
        finally
        {
            try { File.Delete(path); } catch { /* geçici dosya temizliği best-effort */ }
        }
    });

    public void Close()
    {
        if (!_connected)
        {
            return;
        }
        _connected = false;
        try
        {
            _sta.Invoke(() =>
            {
                var connection = SapCom.Get(_session, "Parent"); // GuiConnection
                if (connection is not null)
                {
                    SapCom.Invoke(connection, "CloseConnection");
                }
            });
        }
        catch
        {
            // Kapatma hatası yok sayılır.
        }
        finally
        {
            _sta.Dispose();
        }
    }

    private object Find(string elementId)
    {
        try
        {
            return SapCom.Invoke(_session, "findById", elementId)
                ?? throw new SystemException($"SAP GUI elementi bulunamadi: '{elementId}'.");
        }
        catch (Exception ex)
        {
            throw new SystemException($"SAP GUI elementi bulunamadı: '{elementId}'. Detay: {ex.Message}", ex);
        }
    }

    private Task Run(Action action)
    {
        EnsureConnected();
        try
        {
            _sta.Invoke(action);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    private Task<T> Run<T>(Func<T> func)
    {
        EnsureConnected();
        try
        {
            return Task.FromResult(_sta.Invoke(func));
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    private void EnsureConnected()
    {
        if (!_connected)
        {
            throw new SystemException("SAP GUI oturumu kapalı.");
        }
    }

    private static Exception Translate(Exception ex)
        => ex is SystemException ? ex : new SystemException($"SAP GUI işlemi başarısız: {ex.Message}", ex);
}
