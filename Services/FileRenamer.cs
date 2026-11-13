using System;
using System.IO;

namespace PowerRenameAvalonia.Services;

/// <summary>
/// GUI·MCP·실행 취소가 공유하는 단일 이동 루틴.
/// </summary>
public static class FileRenamer
{
    /// <summary>
    /// <paramref name="oldPath"/>를 <paramref name="newPath"/>로 옮긴다.
    /// NTFS는 대소문자를 구분하지 않으므로 대소문자만 다른 변경(file.wav → FILE.wav)은
    /// 임시 이름을 거치는 2-Pass Move로 처리하고, 두 번째 단계가 실패하면 원래 이름으로 되돌린다.
    /// </summary>
    public static void Move(string oldPath, string newPath, bool isDirectory)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
            return;

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            string tempPath = Path.Combine(Path.GetDirectoryName(oldPath) ?? string.Empty, $"__pr_temp_{Guid.NewGuid():N}");
            MoveCore(oldPath, tempPath, isDirectory);
            try
            {
                MoveCore(tempPath, newPath, isDirectory);
            }
            catch
            {
                // 임시 이름으로 방치되지 않도록 원복을 시도한 뒤 원래 예외를 전달한다.
                try { MoveCore(tempPath, oldPath, isDirectory); } catch { /* 원 예외가 더 중요 */ }
                throw;
            }
            return;
        }

        if (File.Exists(newPath) || Directory.Exists(newPath))
            throw new IOException($"이미 대상 {(isDirectory ? "폴더" : "파일")}가 존재합니다: {Path.GetFileName(newPath)}");

        MoveCore(oldPath, newPath, isDirectory);
    }

    private static void MoveCore(string from, string to, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }
}
