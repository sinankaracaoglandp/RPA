namespace RPA.Infrastructure.SAP;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using SystemException = RPA.Domain.Exceptions.SystemException;

/// <summary>
/// Çalışan SAP GUI'ye (SAP Logon) bağlanma ve SAP Scripting COM koleksiyonlarını güvenli okuma
/// yardımcıları. Hem oturum fabrikası (<see cref="ComSapGuiSessionFactory"/>) hem de UI Spy element
/// çözücüsü (<c>ComSapGuiElementResolver</c>) aynı attach yolunu kullanır — tek kaynak.
///
/// <para><b>Apartment:</b> buradaki tüm çağrılar bir STA iş parçacığından
/// (<see cref="SapStaThread"/>) yapılmalıdır.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class SapGuiAutomation
{
    /// <summary>
    /// Çalışan SAP GUI otomasyon nesnesine bağlanır ve Scripting motorunu (GuiApplication) döner.
    /// SAP Logon çalışmıyorsa başlatmayı dener. Bağlanamazsa yönlendirici <see cref="SystemException"/>.
    /// </summary>
    public static object AttachEngine()
    {
        var sapGuiAuto = GetSapGuiAutomationObject(out var progIdError, out var monikerError);
        if (sapGuiAuto is null)
        {
            StartSapLogonIfNeeded();
            sapGuiAuto = WaitForSapGuiAutomationObject(out progIdError, out monikerError);
        }

        if (sapGuiAuto is null)
        {
            throw new SystemException(
                "SAP GUI otomasyon nesnesi alinamadi. SAP Logon baslatilamadi veya automation nesnesini yayinlamadi. " +
                "Agent interaktif kullanici oturumunda calismali, " +
                "Agent/SAP Logon ayni yetki seviyesinde calismali ve GUI Scripting etkin olmali. " +
                $"ProgID sonucu: {progIdError?.Message ?? "uygun kayit yok"}. " +
                $"ROT sonucu: {monikerError?.Message ?? "SAPGUI nesnesi bulunamadi"}.");
        }

        object? engine;
        try
        {
            engine = SapCom.Invoke(sapGuiAuto, "GetScriptingEngine");
        }
        catch (Exception ex)
        {
            throw new SystemException(
                "SAP GUI Scripting motoru alınamadı. SAP Logon > Options > Accessibility & Scripting > Scripting etkin olmalı.", ex);
        }

        if (engine is null)
        {
            throw new SystemException("SAP GUI Scripting devre dışı (istemci veya sunucu tarafında).");
        }

        return engine;
    }

    /// <summary>
    /// Motordaki tüm bağlantıların tüm oturumlarını (GuiSession) döner. UI Spy hangi oturumun
    /// imleç altında olduğunu bilmediğinden hepsini dener.
    /// </summary>
    public static IReadOnlyList<object> EnumerateSessions(object engine)
    {
        var sessions = new List<object>();
        var connections = TryGetProperty(engine, "Children");
        var connectionCount = GetCollectionCount(connections);

        for (var i = 0; i < connectionCount; i++)
        {
            var connection = GetIndexedProperty(engine, "Children", i)
                ?? (connections is null ? null : GetCollectionItem(connections, i));
            if (connection is null)
            {
                continue;
            }

            CollectSessions(connection, "Children", sessions);
            CollectSessions(connection, "Sessions", sessions);
        }

        // "Children" ve "Sessions" çoğu sürümde AYNI oturumları yayınlar; tekrarları at
        // (aksi halde her oturum iki kez taranır ve tanılamada iki kez görünür).
        return DistinctById(sessions);
    }

    private static IReadOnlyList<object> DistinctById(List<object> sessions)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<object>(sessions.Count);

        foreach (var session in sessions)
        {
            var id = TryGetProperty(session, "Id")?.ToString();
            if (string.IsNullOrWhiteSpace(id) || seen.Add(id))
            {
                unique.Add(session);
            }
        }

        return unique;
    }

    private static void CollectSessions(object connection, string propertyName, List<object> into)
    {
        var collection = TryGetProperty(connection, propertyName);
        var count = GetCollectionCount(collection);
        for (var j = 0; j < count; j++)
        {
            var session = GetIndexedProperty(connection, propertyName, j)
                ?? (collection is null ? null : GetCollectionItem(collection, j));
            if (session is not null)
            {
                into.Add(session);
            }
        }
    }

    // ---- COM koleksiyon yardımcıları (SAP Scripting koleksiyonları tek bir erişim desenine uymaz) ----

    /// <summary>
    /// SAP koleksiyonunun eleman sayısı. SAP Scripting koleksiyonları sayıyı sürüme/tipe göre
    /// <c>Count</c> VEYA <c>Length</c> ile yayınlar — ikisi de denenir. Okunamazsa 0.
    /// </summary>
    public static int GetCollectionCount(object? collection)
    {
        if (collection is null)
        {
            return 0;
        }

        foreach (var property in new[] { "Count", "Length" })
        {
            try
            {
                var value = SapCom.Get(collection, property);
                if (value is not null)
                {
                    return Convert.ToInt32(value);
                }
            }
            catch
            {
                // Bu özellik bu tipte yok — sıradakini dene.
            }
        }

        return 0;
    }

    public static object? GetCollectionItem(object collection, int index)
    {
        try { return SapCom.Get(collection, "Item", index); } catch { }
        try { return SapCom.Invoke(collection, "Item", index); } catch { }
        try { return SapCom.Invoke(collection, "ElementAt", index); } catch { }
        return null;
    }

    public static object? GetIndexedProperty(object target, string propertyName, int index)
    {
        try { return SapCom.Get(target, propertyName, index); } catch { }
        try { return SapCom.Invoke(target, propertyName, index); } catch { }
        return null;
    }

    public static object? GetFirstCollectionItem(object? collection)
    {
        if (collection is null || GetCollectionCount(collection) <= 0)
        {
            return null;
        }

        return GetCollectionItem(collection, 0);
    }

    public static object? TryGetProperty(object target, string propertyName)
    {
        try { return SapCom.Get(target, propertyName); } catch { }
        return null;
    }

    // ---- COM interop (Running Object Table üzerinden SAPGUI'ye bağlan) ----

    private static object? WaitForSapGuiAutomationObject(out Exception? progIdError, out Exception? monikerError)
    {
        progIdError = null;
        monikerError = null;

        var deadline = DateTime.UtcNow.AddSeconds(15);
        do
        {
            var sapGuiAuto = GetSapGuiAutomationObject(out progIdError, out monikerError);
            if (sapGuiAuto is not null)
            {
                return sapGuiAuto;
            }

            Thread.Sleep(500);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private static void StartSapLogonIfNeeded()
    {
        if (Process.GetProcessesByName("saplogon").Length > 0 ||
            Process.GetProcessesByName("saplgpad").Length > 0)
        {
            return;
        }

        var path = FindSapLogonPath();
        if (path is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private static string? FindSapLogonPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SAP", "FrontEnd", "SAPGUI", "saplogon.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SAP", "FrontEnd", "SAPGUI", "saplogon.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static object? GetSapGuiAutomationObject(out Exception? progIdError, out Exception? monikerError)
    {
        var fromProgId = TryGetActiveObjectByProgId("SAPGUI", out progIdError);
        if (fromProgId is not null)
        {
            monikerError = null;
            return fromProgId;
        }

        // VBScript GetObject("SAPGUI") esdegeri. SAP GUI 8.x kurulumlarinda
        // SAPGUI ProgID kaydi olmayabilir; calisan nesne ROT display-name ile yayinlanir.
        return TryFindRunningObject("SAPGUI", out monikerError);
    }

    private static object? TryGetActiveObjectByProgId(string progId, out Exception? error)
    {
        error = null;
        try
        {
            if (CLSIDFromProgID(progId, out var clsid) != 0)
            {
                return null;
            }

            GetActiveObject(ref clsid, IntPtr.Zero, out var activeObject);
            return activeObject;
        }
        catch (Exception ex)
        {
            error = ex;
            return null;
        }
    }

    private static object? TryFindRunningObject(string displayName, out Exception? error)
    {
        error = null;
        try
        {
            GetRunningObjectTable(0, out var rot);
            CreateBindCtx(0, out var bindCtx);
            rot.EnumRunning(out var enumMoniker);

            var monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                monikers[0].GetDisplayName(bindCtx, null, out var currentName);
                if (string.Equals(currentName, displayName, StringComparison.OrdinalIgnoreCase) ||
                    currentName.EndsWith(displayName, StringComparison.OrdinalIgnoreCase))
                {
                    rot.GetObject(monikers[0], out var runningObject);
                    return runningObject;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            error = ex;
            return null;
        }
    }

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string lpszProgID, out Guid lpclsid);

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CreateBindCtx(int reserved, out IBindCtx bindCtx);

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void GetRunningObjectTable(int reserved, out IRunningObjectTable runningObjectTable);
}
