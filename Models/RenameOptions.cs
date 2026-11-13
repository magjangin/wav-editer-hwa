namespace PowerRenameAvalonia.Models;

/// <summary>
/// 한 번의 미리보기/적용에 쓰이는 치환 설정 묶음. GUI와 MCP가 같은 규칙을 공유한다.
/// </summary>
public sealed record RenameOptions(
    string SearchText,
    string ReplaceText,
    bool UseRegex = false,
    bool MatchAll = true,
    bool MatchCase = false,
    RenameScope Scope = RenameScope.NameAndExtension,
    TextFormatting Formatting = TextFormatting.None);

/// <summary>
/// 디스크에서 실제로 성공한 이름 변경 한 건 (BMS 동기화·실행 취소의 단일 입력 형식).
/// </summary>
public readonly record struct RenamedPath(string OldPath, string NewPath, bool IsDirectory = false);
