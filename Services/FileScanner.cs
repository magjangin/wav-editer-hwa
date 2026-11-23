using System.IO;

namespace PowerRenameAvalonia.Services;

/// <summary>GUI·MCP가 공유하는 폴더 탐색 옵션.</summary>
public static class FileScanner
{
    /// <summary>
    /// 기존 <c>SearchOption</c> 동작(숨김·시스템 파일 포함, Win32 패턴 매칭)은 유지하면서
    /// 접근 권한이 없는 하위 폴더 하나 때문에 탐색 전체가 중단되지 않도록 건너뛴다.
    /// </summary>
    public static EnumerationOptions Options(bool recurse) => new()
    {
        MatchType = MatchType.Win32,
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = recurse,
        ReturnSpecialDirectories = false
    };
}
