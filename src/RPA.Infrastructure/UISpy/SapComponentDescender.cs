namespace RPA.Infrastructure.UISpy;

/// <summary>
/// SAP bileşen ağacına COM'dan bağımsız erişim (test edilebilirlik seam'i).
/// </summary>
public interface ISapComponentAccessor
{
    /// <summary>Bileşenin doğrudan çocukları (yoksa boş).</summary>
    IReadOnlyList<object> GetChildren(object node);

    /// <summary>Bileşenin ekran dikdörtgeni; okunamazsa <c>null</c>.</summary>
    (int Left, int Top, int Width, int Height)? GetRect(object node);

    /// <summary>
    /// Bir koleksiyon nesnesinin elemanları (nesne koleksiyon değilse boş).
    /// <para><c>FindByPosition</c> sürüme göre tek bileşen VEYA
    /// <c>GuiComponentCollection</c> döndürür; ikisi de aynı şekilde ele alınmalıdır.</para>
    /// </summary>
    IReadOnlyList<object> GetCollectionItems(object node);

    /// <summary>
    /// Bileşenin SAP element ID'si; okunamazsa <c>null</c>.
    /// <para>Adreslenemeyen (ID'si okunamayan) bir bileşen picker sonucu OLAMAZ — üretilen
    /// <c>elementId</c> boş kalır ve aktivite çalışmaz.</para>
    /// </summary>
    string? GetId(object node);
}

/// <summary>
/// SAP <c>FindByPosition</c> sonucunu kullanıcının gerçekten kastettiği elemente indirger.
///
/// <para><b>Neden gerekli:</b> <c>FindByPosition</c> çoğu ekranda noktayı içeren KONTEYNERİ
/// (<c>GuiUserArea</c>, <c>GuiSimpleContainer</c>, subscreen) döndürür. Bu ham sonuç kullanıcıya
/// verilirse, metin alanını göstermek isteyen kişi alanın içinde bulunduğu frame'i seçmiş olur ve
/// üretilen <c>elementId</c> yanlış olur.</para>
/// </summary>
public static class SapComponentDescender
{
    /// <summary>SAP ekran ağacı sığdır; patolojik/döngüsel ağaca karşı emniyet sınırı.</summary>
    public const int DefaultMaxDepth = 24;

    /// <summary>
    /// <paramref name="root"/>'tan başlayarak (<paramref name="x"/>,<paramref name="y"/>) noktasını
    /// içeren ve <b>ID'si okunabilen</b> en derin alt bileşeni döndürür.
    ///
    /// <para>ID kontrolü zorunludur: SAP ağacında noktayı kapsayan ama <c>Id</c>'si okunamayan
    /// ara nesneler bulunabilir; bunlara inildiğinde picker boş <c>elementId</c> üretir
    /// (kullanıcıya "seçildi" der ama alan boş kalır). Böyle bir dala inilirse ID'si okunabilen
    /// en son ataya geri düşülür.</para>
    ///
    /// <para>Hiçbir çocuk noktayı içermiyorsa (veya konumları okunamıyorsa) <paramref name="root"/>
    /// döner — asla null dönmez.</para>
    /// </summary>
    public static object Deepest(
        object root,
        int x,
        int y,
        ISapComponentAccessor accessor,
        int maxDepth = DefaultMaxDepth)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(accessor);

        var current = root;
        // Adreslenebilir (ID'si okunabilen) en derin aday. Kök adreslenemiyorsa bile en azından
        // onu döndürürüz; çağıran boş ID'yi ayrıca reddeder.
        var bestAddressable = HasId(root, accessor) ? root : null;

        for (var depth = 0; depth < maxDepth; depth++)
        {
            var next = FirstChildContaining(current, x, y, accessor);
            if (next is null)
            {
                break;
            }

            current = next;
            if (HasId(current, accessor))
            {
                bestAddressable = current;
            }
        }

        return bestAddressable ?? current;
    }

    private static bool HasId(object node, ISapComponentAccessor accessor)
        => !string.IsNullOrWhiteSpace(accessor.GetId(node));

    private static object? FirstChildContaining(object node, int x, int y, ISapComponentAccessor accessor)
    {
        object? best = null;
        long bestArea = long.MaxValue;

        foreach (var child in accessor.GetChildren(node))
        {
            if (accessor.GetRect(child) is not { } rect || !Contains(rect, x, y))
            {
                continue;
            }

            // Çakışan kardeşlerde (SAP'ta konteyner + içindeki alan aynı noktayı kapsayabilir)
            // en küçük alanlı olan kullanıcının kastettiği elemente en yakınıdır.
            var area = (long)rect.Width * rect.Height;
            if (area < bestArea)
            {
                best = child;
                bestArea = area;
            }
        }

        return best;
    }

    private static bool Contains((int Left, int Top, int Width, int Height) rect, int x, int y)
        => x >= rect.Left
           && x < rect.Left + rect.Width
           && y >= rect.Top
           && y < rect.Top + rect.Height;

    /// <summary>
    /// <c>FindByPosition</c> sonucunu gerçek bileşene açar.
    ///
    /// <para><b>Neden gerekli:</b> çağrı sürüme göre ya bileşeni ya da bir
    /// <c>GuiComponentCollection</c> döndürür — SAP dışı bir noktada ise BOŞ koleksiyon döndürür.
    /// Koleksiyon nesnesi bileşen sanılırsa <c>Id</c> okunamaz ve picker "bileşen bulundu ama
    /// adreslenemiyor" durumuna düşer (saha kanıtı: SAP penceresinin tamamen dışındaki noktalarda
    /// bile boş olmayan nesne dönüyordu).</para>
    ///
    /// <para>Tip tahmini yapılmaz; ölçüt <b>ID'nin okunabilmesidir</b>. Koleksiyonda en içteki
    /// (son) adreslenebilir eleman tercih edilir. Hiçbiri adreslenemezse <c>null</c>.</para>
    /// </summary>
    public static object? Unwrap(object? found, ISapComponentAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        if (found is null)
        {
            return null;
        }

        // 1) Zaten adreslenebilir bir bileşen mi?
        if (HasId(found, accessor))
        {
            return found;
        }

        // 2) Koleksiyon ise: en içteki (son) adreslenebilir elemanı al.
        var items = accessor.GetCollectionItems(found);
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (HasId(items[i], accessor))
            {
                return items[i];
            }
        }

        // 3) Ne bileşen ne de adreslenebilir eleman içeren koleksiyon → sonuç yok.
        return null;
    }
}
