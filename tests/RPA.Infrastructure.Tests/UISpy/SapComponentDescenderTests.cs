namespace RPA.Infrastructure.Tests.UISpy;

using RPA.Infrastructure.UISpy;

/// <summary>
/// SAP UI Spy "hedef göster" derinlik seçimi. Regresyon kaynağı: <c>FindByPosition</c> noktayı
/// içeren konteyneri döndürdüğünde, kullanıcı metin alanını göstermek isterken alanın içinde
/// bulunduğu frame seçiliyordu.
/// </summary>
public class SapComponentDescenderTests
{
    /// <summary>Test ağacı düğümü (gerçek SAP COM bileşeninin yerine geçer). <paramref name="Id"/> boş ise SAP'ta ID'si okunamayan
    /// (adreslenemeyen) bir bileşeni temsil eder.</summary>
    private sealed record Node(string Id, int Left, int Top, int Width, int Height, params Node[] Children)
    {
        /// <summary>Koleksiyon nesnesini temsil eden düğüm (FindByPosition'ın döndürdüğü gibi).</summary>
        public Node[] Items { get; init; } = Array.Empty<Node>();
    }

    private sealed class FakeAccessor : ISapComponentAccessor
    {
        public IReadOnlyList<object> GetChildren(object node) => ((Node)node).Children;

        public IReadOnlyList<object> GetCollectionItems(object node) => ((Node)node).Items;

        public (int Left, int Top, int Width, int Height)? GetRect(object node)
        {
            var n = (Node)node;
            return (n.Left, n.Top, n.Width, n.Height);
        }

        public string? GetId(object node)
        {
            var id = ((Node)node).Id;
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
    }

    /// <summary>Konumu okunamayan (SAP'ta konum vermeyen) bileşenleri taklit eder.</summary>
    private sealed class RectlessAccessor : ISapComponentAccessor
    {
        public IReadOnlyList<object> GetChildren(object node) => ((Node)node).Children;

        public IReadOnlyList<object> GetCollectionItems(object node) => ((Node)node).Items;

        public (int Left, int Top, int Width, int Height)? GetRect(object node) => null;

        public string? GetId(object node) => ((Node)node).Id;
    }

    private static string Deepest(Node root, int x, int y, ISapComponentAccessor? accessor = null)
        => ((Node)SapComponentDescender.Deepest(root, x, y, accessor ?? new FakeAccessor())).Id;

    [Fact]
    public void Deepest_PicksTextField_NotContainingFrame()
    {
        // Kullanıcının bildirdiği senaryo: frame içindeki metin alanına tıklanır.
        var textField = new Node("wnd[0]/usr/subSUB:SAPLMGMM:0001/ctxtRMMG1-MATNR", 100, 200, 120, 20);
        var frame = new Node("wnd[0]/usr/subSUB:SAPLMGMM:0001", 50, 150, 400, 300, textField);
        var userArea = new Node("wnd[0]/usr", 0, 100, 800, 500, frame);

        Assert.Equal("wnd[0]/usr/subSUB:SAPLMGMM:0001/ctxtRMMG1-MATNR", Deepest(userArea, 110, 205));
    }

    [Fact]
    public void Deepest_WhenPointOutsideAnyChild_ReturnsContainerItself()
    {
        // Frame'in boş bir yerine tıklanırsa frame'in kendisi doğru sonuçtur.
        var textField = new Node("ctxtRMMG1-MATNR", 100, 200, 120, 20);
        var frame = new Node("subSUB", 50, 150, 400, 300, textField);

        Assert.Equal("subSUB", Deepest(frame, 60, 160));
    }

    [Fact]
    public void Deepest_WithOverlappingSiblings_PrefersSmallestArea()
    {
        // SAP'ta konteyner ile içindeki alan aynı noktayı kapsayabilir; kullanıcının kastettiği
        // her zaman daha küçük (daha spesifik) olandır.
        var wide = new Node("wide-container", 0, 0, 500, 400);
        var narrow = new Node("ctxtTARGET", 100, 100, 80, 20);
        var root = new Node("wnd[0]/usr", 0, 0, 800, 600, wide, narrow);

        Assert.Equal("ctxtTARGET", Deepest(root, 110, 105));
    }

    [Fact]
    public void Deepest_DescendsMultipleLevels()
    {
        var leaf = new Node("leaf", 10, 10, 10, 10);
        var mid = new Node("mid", 5, 5, 40, 40, leaf);
        var outer = new Node("outer", 0, 0, 100, 100, mid);

        Assert.Equal("leaf", Deepest(outer, 12, 12));
    }

    [Fact]
    public void Deepest_WhenChildRectsUnreadable_ReturnsRoot()
    {
        // Konum okunamıyorsa inmek tahmin olur — ham sonuç korunur (null DÖNMEZ).
        var child = new Node("child", 10, 10, 10, 10);
        var root = new Node("root", 0, 0, 100, 100, child);

        Assert.Equal("root", Deepest(root, 12, 12, new RectlessAccessor()));
    }

    [Fact]
    public void Deepest_StopsAtMaxDepth_OnPathologicalTree()
    {
        // Her düğümü kendi çocuğu olan döngüsel ağaç: sonsuz döngüye girmemeli.
        Node? self = null;
        var accessor = new SelfReferencingAccessor(() => self!);
        self = new Node("loop", 0, 0, 100, 100);

        var result = SapComponentDescender.Deepest(self, 10, 10, accessor, maxDepth: 5);

        Assert.Same(self, result);
    }

    private sealed class SelfReferencingAccessor(Func<Node> node) : ISapComponentAccessor
    {
        public IReadOnlyList<object> GetChildren(object n) => new object[] { node() };

        public IReadOnlyList<object> GetCollectionItems(object n) => Array.Empty<object>();

        public (int Left, int Top, int Width, int Height)? GetRect(object n) => (0, 0, 100, 100);

        public string? GetId(object n) => ((Node)n).Id;
    }

    [Fact]
    public void Deepest_SkipsUnaddressableChild_AndFallsBackToNearestIdentifiableAncestor()
    {
        // Saha kanıtı: picker "element seçildi  (null)" dedi — noktayı kapsayan ama Id'si
        // okunamayan bir bileşene inilmişti ve boş elementId üretiliyordu.
        var idless = new Node("", 100, 200, 40, 10);
        var textField = new Node("wnd[0]/usr/ctxtRMMG1-MATNR", 100, 200, 120, 20, idless);
        var frame = new Node("wnd[0]/usr/subSUB", 50, 150, 400, 300, textField);

        Assert.Equal("wnd[0]/usr/ctxtRMMG1-MATNR", Deepest(frame, 110, 205));
    }

    [Fact]
    public void Deepest_WhenWholeBranchIsUnaddressable_KeepsIdentifiableRoot()
    {
        var idlessLeaf = new Node("", 12, 12, 5, 5);
        var idlessMid = new Node("   ", 10, 10, 30, 30, idlessLeaf);
        var root = new Node("wnd[0]/usr", 0, 0, 100, 100, idlessMid);

        Assert.Equal("wnd[0]/usr", Deepest(root, 13, 13));
    }

    [Fact]
    public void Deepest_StillDescendsWhenDeeperNodeIsAddressable()
    {
        // Ara nesnenin ID'si okunamasa bile ALTINDAKİ adreslenebilir alana inilmelidir.
        var target = new Node("wnd[0]/usr/ctxtTARGET", 12, 12, 6, 6);
        var idlessMid = new Node("", 10, 10, 30, 30, target);
        var root = new Node("wnd[0]/usr", 0, 0, 100, 100, idlessMid);

        Assert.Equal("wnd[0]/usr/ctxtTARGET", Deepest(root, 13, 13));
    }

    // =============================================================== Unwrap (FindByPosition sonucu)

    [Fact]
    public void Unwrap_WhenResultIsAlreadyAComponent_ReturnsIt()
    {
        var component = new Node("wnd[0]/usr/ctxtRMMG1-MATNR", 0, 0, 10, 10);

        Assert.Same(component, SapComponentDescender.Unwrap(component, new FakeAccessor()));
    }

    [Fact]
    public void Unwrap_WhenResultIsCollection_ReturnsInnermostAddressableItem()
    {
        var outer = new Node("wnd[0]/usr", 0, 0, 100, 100);
        var inner = new Node("wnd[0]/usr/ctxtRMMG1-MATNR", 10, 10, 20, 10);
        var collection = new Node("", 0, 0, 0, 0) { Items = new[] { outer, inner } };

        Assert.Same(inner, SapComponentDescender.Unwrap(collection, new FakeAccessor()));
    }

    [Fact]
    public void Unwrap_WhenCollectionIsEmpty_ReturnsNull()
    {
        // Saha kanıtı: SAP penceresinin TAMAMEN DIŞINDAKİ noktalarda (Windows Terminal üzerinde)
        // FindByPosition boş bir koleksiyon döndürüyordu. Koleksiyon bileşen sanılınca picker
        // "bileşen bulundu ama adreslenemiyor" durumuna düşüyor ve seçim hiç çalışmıyordu.
        var empty = new Node("", 0, 0, 0, 0);

        Assert.Null(SapComponentDescender.Unwrap(empty, new FakeAccessor()));
    }

    [Fact]
    public void Unwrap_SkipsUnaddressableItems_InCollection()
    {
        var idless = new Node("", 10, 10, 20, 10);
        var real = new Node("wnd[0]/usr/ctxtTARGET", 10, 10, 20, 10);
        var collection = new Node("", 0, 0, 0, 0) { Items = new[] { real, idless } };

        Assert.Same(real, SapComponentDescender.Unwrap(collection, new FakeAccessor()));
    }

    [Fact]
    public void Unwrap_WhenNull_ReturnsNull()
    {
        Assert.Null(SapComponentDescender.Unwrap(null, new FakeAccessor()));
    }
}
