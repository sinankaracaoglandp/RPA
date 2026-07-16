namespace RPA.Agent.Connectivity;

using System.Text.Json;

/// <summary>Outbox kaydı: anahtar + taşınacak yük.</summary>
/// <param name="Key">Idempotans anahtarı — aynı anahtar bir kez saklanır ve bir kez teslim edilir.</param>
/// <param name="Payload">Serileştirilmiş yük (log/sonuç). GÜVENLİK: credential/token yazılmaz.</param>
public sealed record AgentOutboxEntry(string Key, string Payload);

/// <summary>
/// Outbox kapasitesi doldu (Task 6). AÇIK bir hatadır: sessizce en eski kayıt düşürülmez,
/// çünkü kaybolan bir sonuç/log sessiz veri kaybıdır.
/// </summary>
public sealed class AgentOutboxOverflowException : RPA.Domain.Exceptions.SystemException
{
    public AgentOutboxOverflowException(int capacity)
        : base($"Ajan outbox kapasitesi doldu ({capacity} kayıt). Bağlantı dönene kadar yeni kayıt kabul edilmiyor.")
        => Capacity = capacity;

    /// <summary>Yapılandırılmış azami kayıt sayısı.</summary>
    public int Capacity { get; }
}

/// <summary>
/// Sınırlı, kalıcı yerel outbox (Task 6): bağlantı yokken log ve sonuçlar buraya yazılır,
/// bağlantı dönünce idempotent biçimde flush edilir.
/// </summary>
/// <remarks>
/// <para><b>Idempotans:</b> kayıtlar anahtarla tutulur. Aynı anahtarın tekrar kuyruğa alınması
/// yinelenen kayıt üretmez; onaylanmış (<see cref="Acknowledge"/>) anahtarın tekrar onaylanması
/// no-op'tur → tekrarlanan flush'lar güvenlidir.</para>
/// <para><b>Kalıcılık:</b> her değişiklikte tüm küme geçici dosyaya yazılıp
/// <see cref="File.Move(string, string, bool)"/> ile ATOMİK olarak yerine konur — yarım yazılmış
/// dosya bırakmaz. Bozuk/okunamayan dosya boş outbox sayılır (ajan yine de başlar).</para>
/// </remarks>
public sealed class AgentOutbox
{
    private const int DefaultCapacity = 500;

    private readonly string _filePath;
    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    public AgentOutbox(string filePath, int capacity = DefaultCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _filePath = filePath;
        _capacity = capacity;
        Load();
    }

    /// <summary>Bekleyen kayıt sayısı.</summary>
    public int Count { get { lock (_sync) { return _order.Count; } } }

    /// <summary>Yapılandırılmış azami kayıt sayısı.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Kaydı kuyruğa alır. Anahtar zaten varsa hiçbir şey yapmaz (idempotent).
    /// Kapasite dolu ve anahtar yeniyse <see cref="AgentOutboxOverflowException"/> fırlatır.
    /// </summary>
    public void Enqueue(string key, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_sync)
        {
            if (_entries.ContainsKey(key))
            {
                return; // aynı anahtar → kapasite tüketmez, kopya üretmez
            }
            if (_order.Count >= _capacity)
            {
                throw new AgentOutboxOverflowException(_capacity);
            }
            _entries[key] = payload;
            _order.Add(key);
            Persist();
        }
    }

    /// <summary>Bekleyen kayıtları eklenme sırasıyla döndürür (kaldırmaz).</summary>
    public IReadOnlyList<AgentOutboxEntry> Peek()
    {
        lock (_sync)
        {
            return _order.Select(k => new AgentOutboxEntry(k, _entries[k])).ToList();
        }
    }

    /// <summary>
    /// Sunucunun aldığını onayladığı anahtarları kaldırır. Bilinmeyen/zaten kaldırılmış
    /// anahtarlar sessizce yok sayılır → tekrarlanan flush güvenlidir.
    /// </summary>
    public void Acknowledge(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        lock (_sync)
        {
            var removed = false;
            foreach (var key in keys)
            {
                if (key is not null && _entries.Remove(key))
                {
                    _order.Remove(key);
                    removed = true;
                }
            }
            if (removed)
            {
                Persist();
            }
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }
        try
        {
            var json = File.ReadAllText(_filePath);
            var stored = JsonSerializer.Deserialize<List<AgentOutboxEntry>>(json);
            if (stored is null)
            {
                return;
            }
            foreach (var entry in stored)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && _entries.TryAdd(entry.Key, entry.Payload))
                {
                    _order.Add(entry.Key);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Bozuk/erişilemeyen outbox ajanı başlatmamalı — boş kabul edilir.
            _entries.Clear();
            _order.Clear();
        }
    }

    /// <summary>Tüm kümeyi geçici dosyaya yazıp atomik olarak yerine taşır.</summary>
    private void Persist()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var snapshot = _order.Select(k => new AgentOutboxEntry(k, _entries[k])).ToList();
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
        File.Move(temp, _filePath, overwrite: true);
    }
}
