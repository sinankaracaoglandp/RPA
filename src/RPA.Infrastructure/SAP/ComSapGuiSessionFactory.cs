namespace RPA.Infrastructure.SAP;

using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Gerçek SAP GUI Scripting (COM) oturumu üreten fabrika. Çalışan SAP Logon'a (ProgID "SAPGUI")
/// bağlanır, hedef sistem için bir bağlantı açar ve kimlik bilgileriyle oturum açar.
///
/// <para><b>Ön koşul:</b> Makinede SAP GUI kurulu, SAP Logon açık ve GUI Scripting etkin olmalı
/// (SAP Logon &gt; Options &gt; Accessibility &amp; Scripting &gt; Scripting). Aksi halde açık ve
/// yönlendirici bir <see cref="SystemException"/> fırlatılır.</para>
///
/// <para><b>Apartment:</b> SAP GUI Scripting COM nesneleri STA gerektirir. Tüm COM etkileşimi
/// oturuma özel bir STA iş parçacığında (<see cref="SapStaThread"/>) marshallanır.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ComSapGuiSessionFactory : ISapGuiSessionFactory
{
    private readonly ILogger<ComSapGuiSessionFactory> _logger;

    public ComSapGuiSessionFactory(ILogger<ComSapGuiSessionFactory> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public ISapGuiSession Create(string systemId, string client, string userId, string password, string language)
    {
        var sta = new SapStaThread();
        try
        {
            var (session, id) = sta.Invoke(() => Connect(systemId, client, userId, password, language));
            _logger.LogInformation("SAP GUI (COM) oturumu açıldı: {SystemId}/{Client} ({SessionId}).", systemId, client, id);
            return new ComSapGuiSession(sta, session, id);
        }
        catch
        {
            sta.Dispose();
            throw;
        }
    }

    private static (object session, string id) Connect(
        string systemId, string client, string userId, string password, string language)
    {
        var engine = AttachEngine();

        object connection;
        try
        {
            connection = SapCom.Invoke(engine, "OpenConnection", systemId, true)
                ?? throw new SystemException($"SAP baglantisi acilamadi: '{systemId}'. OpenConnection null dondu.");
        }
        catch (Exception ex)
        {
            throw new SystemException(
                $"SAP bağlantısı açılamadı: '{systemId}'. SAP Logon'da bu isimde bir bağlantı girdisi olmalı. Detay: {ex.Message}", ex);
        }

        var session = WaitForFirstSession(engine, connection);

        TryLogin(session, client, userId, password, language);

        var id = $"{systemId}/{client}/{Guid.NewGuid():N}";
        id = id.Length > 24 ? id[..24] : id;
        return ((object)session, id);
    }

    private static void TryLogin(object session, string client, string userId, string password, string language)
    {
        // Giriş ekranı alanları varsa doldur; SSO/zaten açık oturumda alanlar bulunmaz (yok sayılır).
        try
        {
            WaitForElement(session, "wnd[0]", TimeSpan.FromSeconds(15));
            SetIfPresent(session, "wnd[0]/usr/txtRSYST-MANDT", client);
            SetIfPresent(session, "wnd[0]/usr/txtRSYST-BNAME", userId);
            SetIfPresent(session, "wnd[0]/usr/pwdRSYST-BCODE", password);
            if (!string.IsNullOrWhiteSpace(language))
            {
                SetIfPresent(session, "wnd[0]/usr/txtRSYST-LANGU", language);
            }
            var mainWindow = SapCom.Invoke(session, "findById", "wnd[0]");
            if (mainWindow is not null)
            {
                SapCom.Invoke(mainWindow, "sendVKey", 0);
            }
        }
        catch
        {
            // Giriş ekranı yok (muhtemelen zaten oturum açık) — devam.
        }

        // Çoklu oturum uyarısı (wnd[1]) çıkarsa "bu oturumla devam" seçip onayla (best-effort).
        try
        {
            var popup = SapCom.Invoke(session, "findById", "wnd[1]");
            try
            {
                var option = SapCom.Invoke(session, "findById", "wnd[1]/usr/radMULTI_LOGON_OPT1");
                if (option is not null)
                {
                    SapCom.Invoke(option, "Select");
                }
            }
            catch { }
            if (popup is not null)
            {
                SapCom.Invoke(popup, "sendVKey", 0);
            }
        }
        catch
        {
            // Uyarı yok.
        }
    }

    private static object WaitForFirstSession(object engine, object connection)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastError = null;
        string? lastState = null;

        do
        {
            try
            {
                lastState = DescribeSessionState(engine, connection);
                var session = TryGetFirstSession(connection) ?? TryGetFirstSessionFromApplication(engine);
                if (session is not null)
                {
                    return session;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(500);
        }
        while (DateTime.UtcNow < deadline);

        var message = BuildMissingSessionMessage(engine, connection);
        if (!string.IsNullOrWhiteSpace(lastState))
        {
            message += $" SAP scripting durumu: {lastState}";
        }

        throw lastError is null
            ? new SystemException(message)
            : new SystemException(message, lastError);
    }

    private static string BuildMissingSessionMessage(object engine, object connection)
    {
        if (GetCollectionCount(TryGetProperty(engine, "Children")) > 0 &&
            CountApplicationSessions(engine, connection) == 0)
        {
            return "SAP baglantisi acildi ancak SAP GUI Scripting hic session yayinlamiyor. " +
                   "Bu durumda sorun systemId aramasindan cok SAP GUI scripting yetkisi/guvenlik penceresi/Windows oturumu farkidir. " +
                   "SAP GUI Options > Accessibility & Scripting > Scripting altinda scripting acik olmali; " +
                   "script attach/open bildirimleri kapali olmali; Agent ve SAP Logon ayni Windows kullanicisi ve ayni yetki seviyesinde calismali. " +
                   "SAP sunucusunda sapgui/user_scripting parametresi kapaliysa BASIS tarafinda acilmalidir.";
        }

        return "SAP baglantisi acildi ancak login session'i hazir olmadi. SAP Logon sistem ekraninda kaldiysa systemId SAP Logon kaydi ile birebir ayni olmali.";
    }

    private static int CountApplicationSessions(object engine, object openedConnection)
    {
        var total = 0;
        var connections = TryGetProperty(engine, "Children");
        var connectionCount = GetCollectionCount(connections);
        for (var i = 0; i < connectionCount; i++)
        {
            var connection = GetIndexedProperty(engine, "Children", i) ?? GetCollectionItem(connections!, i);
            if (connection is null)
            {
                continue;
            }

            total += GetCollectionCount(TryGetProperty(connection, "Children"));
            total += GetCollectionCount(TryGetProperty(connection, "Sessions"));
        }

        total += GetCollectionCount(TryGetProperty(openedConnection, "Children"));
        total += GetCollectionCount(TryGetProperty(openedConnection, "Sessions"));
        return total;
    }

    private static object? TryGetFirstSessionFromApplication(object engine)
    {
        var connections = TryGetProperty(engine, "Children");
        var connectionCount = GetCollectionCount(connections);
        for (var i = 0; i < connectionCount; i++)
        {
            var directConnection = GetIndexedProperty(engine, "Children", i);
            if (directConnection is not null)
            {
                var directSession = TryGetFirstSession(directConnection);
                if (directSession is not null)
                {
                    return directSession;
                }
            }
        }

        for (var i = 0; i < connectionCount; i++)
        {
            var connection = GetCollectionItem(connections!, i);
            if (connection is null)
            {
                continue;
            }

            var session = TryGetFirstSession(connection);
            if (session is not null)
            {
                return session;
            }
        }

        return null;
    }

    private static string DescribeSessionState(object engine, object connection)
    {
        var engineChildren = TryGetProperty(engine, "Children");
        var connectionChildren = TryGetProperty(connection, "Children");
        var connectionSessions = TryGetProperty(connection, "Sessions");

        var directConnection = GetIndexedProperty(engine, "Children", 0);
        var directChildSession = GetIndexedProperty(connection, "Children", 0);
        var directNamedSession = GetIndexedProperty(connection, "Sessions", 0);

        return string.Join("; ", new[]
        {
            $"engine.Children.Count={DescribeCount(engineChildren)}",
            $"engine.Children(0)={(directConnection is null ? "null" : "ok")}",
            $"engine.Children sessions={DescribeApplicationConnections(engine)}",
            $"connection.Children.Count={DescribeCount(connectionChildren)}",
            $"connection.Children(0)={(directChildSession is null ? "null" : "ok")}",
            $"connection.Sessions.Count={DescribeCount(connectionSessions)}",
            $"connection.Sessions(0)={(directNamedSession is null ? "null" : "ok")}"
        });
    }

    private static string DescribeApplicationConnections(object engine)
    {
        var connections = TryGetProperty(engine, "Children");
        var connectionCount = GetCollectionCount(connections);
        if (connectionCount <= 0)
        {
            return "none";
        }

        var parts = new List<string>();
        for (var i = 0; i < connectionCount; i++)
        {
            var connection = GetIndexedProperty(engine, "Children", i) ?? GetCollectionItem(connections!, i);
            if (connection is null)
            {
                parts.Add($"{i}:null");
                continue;
            }

            parts.Add($"{i}:Children={DescribeCount(TryGetProperty(connection, "Children"))},Sessions={DescribeCount(TryGetProperty(connection, "Sessions"))}");
        }

        return string.Join("|", parts);
    }

    private static string DescribeCount(object? collection)
    {
        if (collection is null)
        {
            return "null";
        }

        try
        {
            return Convert.ToString(SapCom.Get(collection, "Count") ?? "null") ?? "null";
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static object? TryGetFirstSession(object connection)
    {
        var directSession = GetIndexedProperty(connection, "Children", 0);
        if (directSession is not null)
        {
            return directSession;
        }

        directSession = GetIndexedProperty(connection, "Sessions", 0);
        if (directSession is not null)
        {
            return directSession;
        }

        var children = TryGetProperty(connection, "Children");
        var session = GetFirstCollectionItem(children);
        if (session is not null)
        {
            return session;
        }

        var sessions = TryGetProperty(connection, "Sessions");
        return GetFirstCollectionItem(sessions);
    }

    // COM koleksiyon erişimi ve SAPGUI attach yolu UI Spy element çözücüsüyle paylaşılır
    // (tek kaynak: SapGuiAutomation); aşağıdakiler yalnızca yönlendiricidir.
    private static int GetCollectionCount(object? collection)
        => SapGuiAutomation.GetCollectionCount(collection);

    private static object? GetCollectionItem(object collection, int index)
        => SapGuiAutomation.GetCollectionItem(collection, index);

    private static object? GetIndexedProperty(object target, string propertyName, int index)
        => SapGuiAutomation.GetIndexedProperty(target, propertyName, index);

    private static object? GetFirstCollectionItem(object? collection)
        => SapGuiAutomation.GetFirstCollectionItem(collection);

    private static object? TryGetProperty(object target, string propertyName)
        => SapGuiAutomation.TryGetProperty(target, propertyName);

    private static object? WaitForElement(object session, string elementId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        do
        {
            try
            {
                var element = SapCom.Invoke(session, "findById", elementId);
                if (element is not null)
                {
                    return element;
                }
            }
            catch
            {
                // Element henuz hazir degil.
            }

            Thread.Sleep(250);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private static void SetIfPresent(object session, string elementId, string value)
    {
        try
        {
            var el = SapCom.Invoke(session, "findById", elementId);
            if (el is not null)
            {
                SapCom.Set(el, "Text", value ?? string.Empty);
            }
        }
        catch
        {
            // Alan yok — yok say.
        }
    }

    private static object AttachEngine() => SapGuiAutomation.AttachEngine();

}

/// <summary>
/// SAP GUI Scripting COM çağrılarını marshalladığı tek bir STA iş parçacığı. Her SAP oturumu
/// kendi STA thread'ine sahiptir; oturuma ait tüm COM etkileşimi bu thread'de yürür.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SapStaThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public SapStaThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "SAP-GUI-STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            work();
        }
    }

    public T Invoke<T>(Func<T> func)
    {
        if (_queue.IsAddingCompleted)
        {
            throw new SystemException("SAP GUI oturumu kapatılmış (STA thread durduruldu).");
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Invoke(Action action) => Invoke<object?>(() => { action(); return null; });

    public void Dispose() => _queue.CompleteAdding();
}
