namespace RPA.Agent.Session;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RPA.Domain.Enums;
using RPA.Domain.Interfaces;

/// <summary>
/// <see cref="ISessionInfoProvider"/> implementasyonu — Windows Terminal Services (WTS) API
/// ile oturumları sorgular (wtsapi32.dll). Etkin konsol oturumu
/// <c>WTSGetActiveConsoleSessionId</c> ile, oturum listesi <c>WTSEnumerateSessions</c> ile alınır.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WtsSessionInfoProvider : ISessionInfoProvider
{
    private static readonly IntPtr ServerHandleCurrent = IntPtr.Zero; // WTS_CURRENT_SERVER_HANDLE

    public SessionInfo GetActiveSession()
    {
        var sessionId = (int)WTSGetActiveConsoleSessionId();
        if (sessionId < 0)
        {
            return new SessionInfo(-1, SessionState.Unknown, null);
        }

        var state = MapState(QueryConnectState(sessionId));
        var userName = QueryUserName(sessionId);
        return new SessionInfo(sessionId, state, userName);
    }

    public IReadOnlyList<SessionInfo> ListSessions()
    {
        var result = new List<SessionInfo>();
        if (!WTSEnumerateSessions(ServerHandleCurrent, 0, 1, out var buffer, out var count))
        {
            return result;
        }

        try
        {
            var dataSize = Marshal.SizeOf<WtsSessionInfo>();
            var current = buffer;
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WtsSessionInfo>(current);
                result.Add(new SessionInfo(
                    info.SessionId,
                    MapState(info.State),
                    QueryUserName(info.SessionId)));
                current += dataSize;
            }
        }
        finally
        {
            WTSFreeMemory(buffer);
        }

        return result;
    }

    private static WtsConnectState QueryConnectState(int sessionId)
    {
        if (WTSQuerySessionInformation(ServerHandleCurrent, sessionId,
                WtsInfoClass.ConnectState, out var buffer, out _))
        {
            try
            {
                return (WtsConnectState)Marshal.ReadInt32(buffer);
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        return WtsConnectState.WTSInit;
    }

    private static string? QueryUserName(int sessionId)
    {
        if (WTSQuerySessionInformation(ServerHandleCurrent, sessionId,
                WtsInfoClass.UserName, out var buffer, out var bytes) && bytes > 0)
        {
            try
            {
                var name = Marshal.PtrToStringUni(buffer);
                return string.IsNullOrEmpty(name) ? null : name;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        return null;
    }

    private static SessionState MapState(WtsConnectState state) => state switch
    {
        WtsConnectState.WTSActive => SessionState.Active,
        WtsConnectState.WTSDisconnected => SessionState.Disconnected,
        WtsConnectState.WTSShadow => SessionState.Active,
        _ => SessionState.Unknown
    };

    // ---- P/Invoke ----

    private enum WtsConnectState
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    private enum WtsInfoClass
    {
        UserName = 5,
        ConnectState = 8
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        [MarshalAs(UnmanagedType.LPWStr)] public string WinStationName;
        public WtsConnectState State;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(
        IntPtr hServer, int reserved, int version, out IntPtr ppSessionInfo, out int pCount);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer, int sessionId, WtsInfoClass infoClass, out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);
}
