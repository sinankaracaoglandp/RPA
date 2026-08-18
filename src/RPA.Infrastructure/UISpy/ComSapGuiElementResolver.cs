namespace RPA.Infrastructure.UISpy;

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RPA.Domain.ValueObjects;
using RPA.Infrastructure.SAP;

/// <summary>
/// <see cref="ISapGuiElementResolver"/>'ın gerçek SAP GUI Scripting (COM) implementasyonu.
/// Çalışan SAP Logon'a <see cref="SapGuiAutomation.AttachEngine"/> ile bağlanır ve verilen ekran
/// noktasındaki elementi <c>GuiSession.FindByPosition</c> ile çözer.
///
/// <para><b>Apartment:</b> SAP scripting STA gerektirir; tüm COM etkileşimi kalıcı bir
/// <see cref="SapStaThread"/> üzerinde marshallanır (oturum fabrikasıyla aynı desen).</para>
///
/// <para><b>Oturum seçimi:</b> hangi SAP oturumunun imleç altında olduğu bilinmediğinden açık tüm
/// oturumlar denenir; noktayı içeren ilk oturumun sonucu kullanılır.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ComSapGuiElementResolver : ISapGuiElementResolver, IDisposable
{
    private readonly ILogger<ComSapGuiElementResolver> _logger;
    private readonly SapStaThread _sta = new();
    private readonly object _gate = new();
    private object? _engine;
    private volatile string? _lastError;
    private object? _lastVisualized;

    /// <inheritdoc />
    public string? LastError => _lastError;

    public ComSapGuiElementResolver(ILogger<ComSapGuiElementResolver> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public SapGuiElement? ResolveAt(int x, int y)
    {
        try
        {
            // Önceki noktanın hatası bu noktaya taşınmasın (tanılama yanıltıcı olur).
            _lastError = null;
            return _sta.Invoke(() => ResolveOnStaThread(x, y));
        }
        catch (Exception ex)
        {
            // UI Spy hover döngüsünde saniyede ~25 kez çağrılır; hata akışı kesmemeli — ama
            // sebebi saklanır ki picker kullanıcıya bildirebilsin (sessiz başarısızlık olmasın).
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            _logger.LogDebug(ex, "SAP UI Spy: ({X},{Y}) çözülemedi.", x, y);
            DropEngine();
            return null;
        }
    }

    public void Highlight(int x, int y)
    {
        try
        {
            _sta.Invoke(() =>
            {
                // ÖNCE önceki çerçeveyi kapat: SAP'ın Visualize çerçevesi kendiliğinden silinmez,
                // aksi halde gezinirken ekranda çerçeveler birikir.
                TurnOffLastVisualized();

                var component = FindComponentAt(x, y);
                if (component is not null)
                {
                    // GuiVComponent.Visualize(true): SAP'ın kendi kırmızı çerçevesi.
                    SapCom.Invoke(component, "Visualize", true);
                    _lastVisualized = component;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "SAP UI Spy: vurgu çizilemedi ({X},{Y}).", x, y);
        }
    }

    /// <inheritdoc />
    public void ClearHighlight()
    {
        try
        {
            _sta.Invoke(TurnOffLastVisualized);
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "SAP UI Spy: vurgu kaldırılamadı.");
        }
    }

    private void TurnOffLastVisualized()
    {
        if (_lastVisualized is null)
        {
            return;
        }

        try
        {
            SapCom.Invoke(_lastVisualized, "Visualize", false);
        }
        catch
        {
            // Ekran değişmiş olabilir (bileşen artık yok) — çerçeve zaten kaybolmuştur.
        }
        finally
        {
            _lastVisualized = null;
        }
    }

    /// <inheritdoc />
    public string SelfTest()
    {
        try
        {
            return _sta.Invoke(SelfTestOnStaThread);
        }
        catch (Exception ex)
        {
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            return $"SAP'a bağlanılamadı — {_lastError}";
        }
    }

    private string SelfTestOnStaThread()
    {
        var sessions = GetSessions();
        if (sessions.Count == 0)
        {
            return "SAP'a attach edildi ancak SAP GUI Scripting hiç OTURUM yayınlamıyor. " +
                   "Scripting istemcide açık olsa bile sunucu tarafında (sapgui/user_scripting) " +
                   "kapalıysa veya Agent ile SAP Logon farklı Windows kullanıcısı/yetki seviyesinde " +
                   "çalışıyorsa bu görülür.";
        }

        var parts = new List<string> { $"{sessions.Count} oturum" };
        for (var i = 0; i < sessions.Count; i++)
        {
            try
            {
                var window = SapCom.Invoke(sessions[i], "findById", "wnd[0]");
                if (window is null)
                {
                    parts.Add($"[{i}] wnd[0] yok");
                    continue;
                }

                var rect = ComAccessor.Instance.GetRect(window);
                var text = AsString(SapGuiAutomation.TryGetProperty(window, "Text")) ?? "-";
                parts.Add(rect is { } r
                    ? $"[{i}] wnd[0] '{text}' ekran dikdörtgeni: x={r.Left}, y={r.Top}, {r.Width}x{r.Height}"
                    : $"[{i}] wnd[0] '{text}' (konum okunamadı)");

                // Kesin ölçüm: pencerenin TAM MERKEZİNDE iki yolu da dene (fare konumundan bağımsız).
                if (rect is { } probe)
                {
                    var cx = probe.Left + (probe.Width / 2);
                    var cy = probe.Top + (probe.Height / 2);

                    var viaTree = FindByWindowTree(new[] { sessions[i] }, cx, cy);
                    parts.Add(viaTree is not null
                        ? $"[{i}] ağaç taraması ({cx},{cy}) → '{ComAccessor.Instance.GetId(viaTree)}'"
                        : $"[{i}] ağaç taraması ({cx},{cy}) → sonuç yok");

                    var failures = new List<string>();
                    var found = InvokeFindByPosition(sessions[i], cx, cy, failures);
                    var component = SapComponentDescender.Unwrap(found, ComAccessor.Instance);
                    parts.Add(component is not null
                        ? $"[{i}] FindByPosition({cx},{cy}) → '{ComAccessor.Instance.GetId(component)}'"
                        : found is not null
                            ? $"[{i}] FindByPosition({cx},{cy}) → sonuç döndü ama adreslenebilir bileşen yok " +
                              $"(eleman sayısı: {SapGuiAutomation.GetCollectionCount(found)})"
                            : $"[{i}] FindByPosition({cx},{cy}) sonuçsuz → " +
                              (failures.Count > 0 ? string.Join(" ; ", failures.Distinct()) : "null döndü, hata yok"));

                    // wnd[0] çocukları: ağaç gezmenin mümkün olup olmadığını KESİN gösterir.
                    // Çocuk yoksa veya ID/konum okunamıyorsa konum→element eşlemesi imkânsızdır.
                    parts.Add(DescribeChildren(window));
                }
            }
            catch (Exception ex)
            {
                parts.Add($"[{i}] wnd[0] okunamadı: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return string.Join(" | ", parts);
    }

    private SapGuiElement? ResolveOnStaThread(int x, int y)
    {
        var component = FindComponentAt(x, y);
        if (component is null)
        {
            return null;
        }

        var element = Describe(component, x, y);

        // Boş ID kullanılamaz: alana yazılsa aktivite çalışmaz. "Seçildi" deyip boş değer
        // döndürmek sessiz başarısızlıktır — bulunamadı say ve sebebi kaydet.
        if (string.IsNullOrWhiteSpace(element.Id))
        {
            _lastError =
                $"SAP bileşeni bulundu ancak Id özelliği okunamadı (tip: '{element.Type ?? "<null>"}', " +
                $"metin: '{element.Text ?? "<null>"}'). Bu bileşen adreslenemez.";
            return null;
        }

        return element;
    }

    private object? FindComponentAt(int x, int y)
    {
        var sessions = GetSessions();
        if (sessions.Count == 0)
        {
            // Sessiz başarısızlık kaynağı: EnumerateSessions COM hatalarını yutar ve BOŞ liste
            // döner. Önbelleklenen motor referansı bayatladığında (SAP kapanıp açıldığında ya da
            // araya başka bir SAP oturumu girdiğinde) hiçbir istisna oluşmadan her nokta
            // "sonuç yok" olur. Motoru bırak → sonraki çağrı yeniden attach eder.
            DropEngine();
            _lastError = "SAP oturumu görünmüyor (motor bayatlamış olabilir; yeniden bağlanılacak).";
            return null;
        }

        // BİRİNCİL YOL: pencere ağacını kendimiz gez.
        //
        // FindByPosition bu ortamda hiçbir noktada sonuç üretmedi (268/268 boş) — ama öz-test
        // findById + ScreenLeft/ScreenTop/Width/Height + Children okumalarının ÇALIŞTIĞINI
        // kanıtladı (wnd[0] dikdörtgeni doğru okundu). Dolayısıyla hit-test'i SAP'a sormaya
        // gerek yok: imleci içeren en derin adreslenebilir bileşeni ağaçtan kendimiz buluruz.
        var byTree = FindByWindowTree(sessions, x, y);
        if (byTree is not null)
        {
            return byTree;
        }

        // İKİNCİL YOL: bazı sürümlerde FindByPosition daha isabetli olabilir (özel kontroller).
        var failures = new List<string>();
        foreach (var session in sessions)
        {
            var found = InvokeFindByPosition(session, x, y, failures);
            if (found is null)
            {
                continue;
            }

            var component = SapComponentDescender.Unwrap(found, ComAccessor.Instance);
            if (component is not null)
            {
                // FindByPosition genelde noktayı içeren KONTEYNERİ döndürür (GuiUserArea,
                // GuiSimpleContainer, subscreen). Kullanıcı metin alanını seçmek isterken frame
                // seçilmesinin sebebi budur — çocuklara inip noktayı içeren en derin bileşeni al.
                return SapComponentDescender.Deepest(component, x, y, ComAccessor.Instance);
            }
        }

        // Tüm çağrılar hata verdiyse bunu "boş sonuç" diye raporlamak yanıltıcıdır — SAP'ın
        // gerçek hatası tanılamaya taşınır.
        if (failures.Count > 0)
        {
            _lastError = "FindByPosition çağrılamadı → " + string.Join(" ; ", failures.Distinct());
        }

        return null;
    }

    /// <summary>
    /// <c>wnd[0]</c>'ın ilk seviye çocuklarını (ID + ekran dikdörtgeni) özetler — ağaç taramasının
    /// çalışıp çalışamayacağının doğrudan kanıtı.
    /// </summary>
    private static string DescribeChildren(object window)
    {
        var children = ComAccessor.Instance.GetChildren(window);
        if (children.Count == 0)
        {
            return "wnd[0] ÇOCUK YAYINLAMIYOR (ağaç taraması imkânsız)";
        }

        var described = children
            .Take(6)
            .Select(child =>
            {
                var id = ComAccessor.Instance.GetId(child) ?? "<id yok>";
                var rect = ComAccessor.Instance.GetRect(child);
                var shortId = id.Length > 40 ? "…" + id[^40..] : id;
                return rect is { } r
                    ? $"{shortId} @({r.Left},{r.Top}) {r.Width}x{r.Height}"
                    : $"{shortId} (konum okunamadı)";
            });

        return $"wnd[0] çocukları ({children.Count}): " + string.Join(" | ", described);
    }

    /// <summary>
    /// Oturumların pencerelerini (<c>wnd[0]</c>, popup'lar <c>wnd[1]</c>…) gezerek noktayı içeren
    /// en derin adreslenebilir bileşeni bulur. Üstteki pencere (en yüksek indeks) önceliklidir —
    /// açık bir iletişim kutusu ana ekranı kapatır.
    /// </summary>
    private static object? FindByWindowTree(IReadOnlyList<object> sessions, int x, int y)
    {
        object? best = null;
        var bestWindowIndex = -1;

        foreach (var session in sessions)
        {
            for (var w = 0; w <= MaxWindowIndex; w++)
            {
                object? window;
                try
                {
                    window = SapCom.Invoke(session, "findById", $"wnd[{w}]");
                }
                catch
                {
                    break; // Bu indeksten sonrası yok.
                }

                if (window is null)
                {
                    break;
                }

                if (ComAccessor.Instance.GetRect(window) is not { } rect ||
                    !Contains(rect, x, y))
                {
                    continue;
                }

                if (w >= bestWindowIndex)
                {
                    best = SapComponentDescender.Deepest(window, x, y, ComAccessor.Instance);
                    bestWindowIndex = w;
                }
            }
        }

        return best;
    }

    /// <summary>SAP'ta eşzamanlı açık pencere sayısı sınırlıdır (wnd[0]…wnd[9]).</summary>
    private const int MaxWindowIndex = 9;

    private static bool Contains((int Left, int Top, int Width, int Height) rect, int x, int y)
        => x >= rect.Left
           && x < rect.Left + rect.Width
           && y >= rect.Top
           && y < rect.Top + rect.Height;

    /// <summary>
    /// <c>GuiSession.FindByPosition</c> çağrısı. SAP sürümleri arasında imza değişir
    /// (<c>(x, y, scrollToElement)</c> veya <c>(x, y)</c>); ikisi de denenir.
    /// <para>Hatalar YUTULMAZ — <paramref name="failures"/>'a yazılır. Aksi halde "hata fırlattı"
    /// ile "boş döndü" ayırt edilemez ve tanılama yanlış yeri gösterir.</para>
    /// </summary>
    private static object? InvokeFindByPosition(object session, int x, int y, List<string> failures)
    {
        // scrollToElement=false: tespit ekranı kaydırmamalı (kullanıcı hedefi kaybetmesin).
        object?[][] argumentSets =
        [
            [x, y, false],
            [x, y],
        ];

        foreach (var args in argumentSets)
        {
            try
            {
                var result = SapCom.Invoke(session, "FindByPosition", args);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{args.Length} argümanlı çağrı: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }

    private static SapGuiElement Describe(object component, int x, int y)
    {
        var rawId = AsString(SapGuiAutomation.TryGetProperty(component, "Id")) ?? string.Empty;

        return new SapGuiElement
        {
            Id = SapElementId.Normalize(rawId),
            Type = AsString(SapGuiAutomation.TryGetProperty(component, "Type")),
            Text = AsString(SapGuiAutomation.TryGetProperty(component, "Text")),
            Enabled = AsBool(SapGuiAutomation.TryGetProperty(component, "Changeable"), fallback: true),
            Changeable = AsBool(SapGuiAutomation.TryGetProperty(component, "Changeable"), fallback: true),
            X = x,
            Y = y,
            Columns = TryReadGridColumns(component),
        };
    }

    /// <summary>
    /// Bileşen bir ALV grid (GuiGridView) ise TASARIM ANINDAKİ teknik kolon adlarını okur.
    /// Grid değilse veya kolonlar okunamazsa <c>null</c> — tip adına göre tahmin yapılmaz,
    /// ölçüt <c>ColumnOrder</c> koleksiyonunun okunabilmesidir.
    /// </summary>
    private static IReadOnlyList<string>? TryReadGridColumns(object component)
    {
        try
        {
            var columnOrder = SapGuiAutomation.TryGetProperty(component, "ColumnOrder");
            if (columnOrder is null)
            {
                return null;
            }

            var count = SapGuiAutomation.GetCollectionCount(columnOrder);
            if (count <= 0)
            {
                return null;
            }

            var columns = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var name = SapGuiAutomation.GetCollectionItem(columnOrder, i)?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    columns.Add(name);
                }
            }

            return columns.Count > 0 ? columns : null;
        }
        catch
        {
            // Grid değil ya da kolon listesi bu kontrolde yok — sessizce geç (kolonsuz seçim geçerlidir).
            return null;
        }
    }

    private static string? AsString(object? value)
    {
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool AsBool(object? value, bool fallback)
        => value is null ? fallback : Convert.ToBoolean(value);

    private object GetEngine()
    {
        lock (_gate)
        {
            return _engine ??= SapGuiAutomation.AttachEngine();
        }
    }

    /// <summary>
    /// Açık SAP oturumları. Önbelleklenen motor oturum yayınlamıyorsa BİR KEZ yeniden attach
    /// edilip tekrar denenir — bayat referans yüzünden picker'ın ölü kalmasını önler.
    /// </summary>
    private IReadOnlyList<object> GetSessions()
    {
        var sessions = SapGuiAutomation.EnumerateSessions(GetEngine());
        if (sessions.Count > 0)
        {
            return sessions;
        }

        DropEngine();
        return SapGuiAutomation.EnumerateSessions(GetEngine());
    }

    /// <summary>SAP kapanıp yeniden açıldığında önbellekli motor referansı ölür — bir dahaki
    /// çağrıda yeniden attach edilsin diye bırakılır.</summary>
    private void DropEngine()
    {
        lock (_gate)
        {
            _engine = null;
        }
    }

    /// <summary><see cref="ISapComponentAccessor"/>'ün SAP Scripting COM implementasyonu.</summary>
    private sealed class ComAccessor : ISapComponentAccessor
    {
        public static ComAccessor Instance { get; } = new();

        public IReadOnlyList<object> GetChildren(object node)
        {
            var children = SapGuiAutomation.TryGetProperty(node, "Children");
            var count = SapGuiAutomation.GetCollectionCount(children);
            if (children is null || count <= 0)
            {
                return Array.Empty<object>();
            }

            var result = new List<object>(count);
            for (var i = 0; i < count; i++)
            {
                var child = SapGuiAutomation.GetCollectionItem(children, i);
                if (child is not null)
                {
                    result.Add(child);
                }
            }

            return result;
        }

        public IReadOnlyList<object> GetCollectionItems(object node)
        {
            var count = SapGuiAutomation.GetCollectionCount(node);
            if (count <= 0)
            {
                return Array.Empty<object>();
            }

            var items = new List<object>(count);
            for (var i = 0; i < count; i++)
            {
                var item = SapGuiAutomation.GetCollectionItem(node, i);
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            return items;
        }

        public string? GetId(object node)
        {
            var id = SapGuiAutomation.TryGetProperty(node, "Id")?.ToString();
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        public (int Left, int Top, int Width, int Height)? GetRect(object node)
        {
            var left = AsInt(SapGuiAutomation.TryGetProperty(node, "ScreenLeft"));
            var top = AsInt(SapGuiAutomation.TryGetProperty(node, "ScreenTop"));
            var width = AsInt(SapGuiAutomation.TryGetProperty(node, "Width"));
            var height = AsInt(SapGuiAutomation.TryGetProperty(node, "Height"));

            return left is null || top is null || width is null || height is null
                ? null
                : (left.Value, top.Value, width.Value, height.Value);
        }

        private static int? AsInt(object? value)
        {
            if (value is null)
            {
                return null;
            }

            try { return Convert.ToInt32(value); } catch { return null; }
        }
    }

    public void Dispose() => _sta.Dispose();
}
