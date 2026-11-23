using System;
using System.IO;
using System.Reflection;

namespace PowerRenameAvalonia;

/// <summary>버전 단일 출처(csproj의 Version)와 시작 실패 기록.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public static void WriteCrashLog(Exception ex)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] v{Version}\n{ex}\n\n");
        }
        catch { /* 기록 실패는 무시 */ }
    }
}
