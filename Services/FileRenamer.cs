using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerRenameAvalonia.Models;

namespace PowerRenameAvalonia.Services;

/// <summary>일괄 이동 한 건의 실행 결과.</summary>
public readonly record struct BatchMoveResult(RenamedPath Plan, string? Error);

/// <summary>
/// GUI·MCP·실행 취소가 공유하는 단일/일괄 이동 루틴.
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
            string tempPath = CreateTempPath(oldPath);
            MoveCore(oldPath, tempPath, isDirectory);
            MoveFromTempWithRollback(tempPath, oldPath, newPath, isDirectory);
            return;
        }

        ThrowIfTargetExists(newPath, isDirectory);
        MoveCore(oldPath, newPath, isDirectory);
    }

    /// <summary>
    /// 여러 파일/폴더를 2단계(스테이징 → 일반 이동 → 최종 이동)로 옮긴다.
    /// <c>a → b, b → c</c> 같은 연쇄 리네임이나 <c>a ↔ b</c> 맞교환, 대소문자 전용 변경도 안전하게 처리한다.
    /// 폴더가 포함된 경우 전달된 깊이 순서대로 계층 단위 웨이브로 실행해 부모·자식 경로가 엇갈리지 않게 한다.
    /// </summary>
    public static List<BatchMoveResult> MoveBatch(IReadOnlyList<RenamedPath> plans)
    {
        var results = new BatchMoveResult[plans.Count];
        if (plans.Count == 0)
            return [];

        bool hasDirectories = plans.Any(p => p.IsDirectory);
        if (!hasDirectories)
        {
            MoveWave(plans.Select((p, i) => (i, p)).ToList(), results);
            return [.. results];
        }

        // 폴더 리네임이 섞여 있으면 같은 깊이끼리 묶어 웨이브 단위로 완료한 뒤 다음 깊이로 넘어간다.
        var wave = new List<(int Index, RenamedPath Plan)>();
        int currentDepth = GetPathDepth(plans[0].OldPath);

        for (int i = 0; i < plans.Count; i++)
        {
            int depth = GetPathDepth(plans[i].OldPath);
            if (depth != currentDepth && wave.Count > 0)
            {
                MoveWave(wave, results);
                wave.Clear();
                currentDepth = depth;
            }
            wave.Add((i, plans[i]));
        }

        if (wave.Count > 0)
            MoveWave(wave, results);

        return [.. results];
    }

    private static void MoveWave(List<(int Index, RenamedPath Plan)> wave, BatchMoveResult[] results)
    {
        var sourceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, plan) in wave)
        {
            if (!string.Equals(plan.OldPath, plan.NewPath, StringComparison.Ordinal))
                sourceSet.Add(plan.OldPath);
        }

        var staged = new List<(int Index, RenamedPath Plan, string TempPath)>();
        var direct = new List<(int Index, RenamedPath Plan)>();

        // 1단계: 대소문자 전용 변경이거나 대상 자리에 다른 이동 예정 파일이 있는 항목은 먼저 임시 이름으로 비켜준다.
        foreach (var (index, plan) in wave)
        {
            if (string.Equals(plan.OldPath, plan.NewPath, StringComparison.Ordinal))
            {
                results[index] = new BatchMoveResult(plan, null);
                continue;
            }

            bool needsStaging = string.Equals(plan.OldPath, plan.NewPath, StringComparison.OrdinalIgnoreCase)
                                || sourceSet.Contains(plan.NewPath);
            if (!needsStaging)
            {
                direct.Add((index, plan));
                continue;
            }

            string tempPath = CreateTempPath(plan.OldPath);
            try
            {
                MoveCore(plan.OldPath, tempPath, plan.IsDirectory);
                staged.Add((index, plan, tempPath));
            }
            catch (Exception ex)
            {
                results[index] = new BatchMoveResult(plan, ex.Message);
            }
        }

        // 1.5단계: 자리가 비워진 뒤 일반 항목을 최종 위치로 이동한다.
        foreach (var (index, plan) in direct)
        {
            try
            {
                ThrowIfTargetExists(plan.NewPath, plan.IsDirectory);
                MoveCore(plan.OldPath, plan.NewPath, plan.IsDirectory);
                results[index] = new BatchMoveResult(plan, null);
            }
            catch (Exception ex)
            {
                results[index] = new BatchMoveResult(plan, ex.Message);
            }
        }

        // 2단계: 임시 이름으로 피해 있던 항목들을 최종 위치로 옮긴다 (실패 시 원복 시도).
        foreach (var (index, plan, tempPath) in staged)
        {
            try
            {
                MoveFromTempWithRollback(tempPath, plan.OldPath, plan.NewPath, plan.IsDirectory);
                results[index] = new BatchMoveResult(plan, null);
            }
            catch (Exception ex)
            {
                results[index] = new BatchMoveResult(plan, ex.Message);
            }
        }
    }

    private static void MoveFromTempWithRollback(string tempPath, string oldPath, string newPath, bool isDirectory)
    {
        try
        {
            ThrowIfTargetExists(newPath, isDirectory);
            MoveCore(tempPath, newPath, isDirectory);
        }
        catch
        {
            // 임시 이름으로 방치되지 않도록 원복을 시도한 뒤 원래 예외를 전달한다.
            try
            {
                if (!File.Exists(oldPath) && !Directory.Exists(oldPath))
                    MoveCore(tempPath, oldPath, isDirectory);
            }
            catch { /* 원 예외가 더 중요 */ }
            throw;
        }
    }

    private static string CreateTempPath(string originalPath)
        => Path.Combine(Path.GetDirectoryName(originalPath) ?? string.Empty, $"__pr_temp_{Guid.NewGuid():N}");

    private static int GetPathDepth(string path)
        => Path.TrimEndingDirectorySeparator(path).Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

    private static void ThrowIfTargetExists(string newPath, bool isDirectory)
    {
        if (File.Exists(newPath) || Directory.Exists(newPath))
            throw new IOException($"이미 대상 {(isDirectory ? "폴더" : "파일")}가 존재합니다: {Path.GetFileName(newPath)}");
    }

    private static void MoveCore(string from, string to, bool isDirectory)
    {
        if (isDirectory)
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }
}
