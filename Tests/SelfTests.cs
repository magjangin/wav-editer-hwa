using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PowerRenameAvalonia.Models;
using PowerRenameAvalonia.Services;
using PowerRenameAvalonia.ViewModels;

namespace PowerRenameAvalonia.Tests;

/// <summary>
/// <c>dotnet run -- --test</c> 로 실행하는 무결성 자체 테스트 (별도 테스트 프로젝트 없이 배포본에서도 실행 가능).
/// </summary>
public static class SelfTests
{
    private static int _passed;

    public static int Run()
    {
        Console.WriteLine("=== RenameEngine ===");
        TestEngine();

        Console.WriteLine("\n=== FileRenamer ===");
        WithTempDir(TestCaseOnlyRename);

        Console.WriteLine("\n=== BmsSyncService ===");
        WithTempDir(TestBmsSyncRelativeAndName);
        WithTempDir(TestBmsPreservesBomAndLineEndings);
        WithTempDir(TestBmsNoChangeNoBackup);
        WithTempDir(TestBmsPrefixDirectoryIsNotParent);

        Console.WriteLine("\n=== MainViewModel (GUI 흐름) ===");
        WithTempDir(TestGuiApplySyncsBmsAndUndoRestores);
        WithTempDir(TestDeselectClearsConflict);

        Console.WriteLine($"\nAll {_passed} tests passed successfully!");
        return 0;
    }

    private static void TestEngine()
    {
        static string T(string name, RenameOptions o) => RenameEngine.Transform(name, false, o, out _);

        AssertEqual(T("000201_하트 지상_dt1.48.wav", new("_dt", "_tempo")),
            "000201_하트 지상_tempo1.48.wav", "Simple replace");
        AssertEqual(T("000201_하트 지상_dt1.48.wav", new(@"_dt(\d+\.\d+)", "_speed$1", UseRegex: true)),
            "000201_하트 지상_speed1.48.wav", "Regex capture group");
        AssertEqual(T("MyAudioFile.WAV", new("Audio", "Sound", Scope: RenameScope.NameOnly, Formatting: TextFormatting.Lowercase)),
            "mysoundfile.WAV", "NameOnly scope + Lowercase formatting");
        AssertEqual(T("sample.wav", new("wav", "ogg", Scope: RenameScope.ExtensionOnly)),
            "sample.ogg", "ExtensionOnly scope");
        AssertEqual(T("abc_abc_abc.wav", new("abc", "xyz", MatchAll: false)),
            "xyz_abc_abc.wav", "Match first occurrence only");
        AssertEqual(T("ABC_abc.wav", new("abc", "x", MatchCase: true)),
            "ABC_x.wav", "Match case");

        var bad = RenameEngine.Create(new RenameOptions("(", "x", UseRegex: true));
        AssertEqual(bad.Transform("a(b.wav", false, out var err), "a(b.wav", "Invalid regex keeps original name");
        AssertTrue(err?.StartsWith("정규식 오류") == true, "Invalid regex reports error");

        // 폴더는 Scope와 무관하게 이름 전체가 대상
        AssertEqual(RenameEngine.Transform("scene.v2", true, new RenameOptions("v2", "v3", Scope: RenameScope.NameOnly), out _),
            "scene.v3", "Directory ignores extension scope");
    }

    private static void TestCaseOnlyRename(string dir)
    {
        string oldPath = Path.Combine(dir, "kick.wav");
        string newPath = Path.Combine(dir, "KICK.wav");
        File.WriteAllText(oldPath, "x");

        FileRenamer.Move(oldPath, newPath, false);
        AssertEqual(Path.GetFileName(Directory.GetFiles(dir).Single()), "KICK.wav", "Case-only rename (2-pass move)");

        File.WriteAllText(Path.Combine(dir, "snare.wav"), "y");
        AssertThrows<IOException>(() => FileRenamer.Move(newPath, Path.Combine(dir, "snare.wav"), false),
            "Refuses to overwrite existing target");
    }

    private static void TestBmsSyncRelativeAndName(string dir)
    {
        string bms = Path.Combine(dir, "hwa2.bms");
        File.WriteAllText(bms,
            "#TITLE test\n#WAV001 10번 씬 wav폴더\\000201_하트 지상_dt1.48.wav\n#WAV002 bgm.wav\n#wav003 other/000201_하트 지상_dt1.48.wav\n",
            new UTF8Encoding(false));

        var renames = new[]
        {
            new RenamedPath(Path.Combine(dir, "10번 씬 wav폴더", "000201_하트 지상_dt1.48.wav"),
                            Path.Combine(dir, "10번 씬 wav폴더", "000201_하트 지상_tempo1.48.wav"))
        };

        int synced = BmsSyncService.SyncBmsFile(bms, renames, out var err);
        AssertEqual(err ?? "", "", "BMS sync has no error");
        AssertEqual(synced.ToString(), "2", "BMS synced relative + filename-matched lines");

        string text = File.ReadAllText(bms);
        AssertTrue(text.Contains("#WAV001 10번 씬 wav폴더\\000201_하트 지상_tempo1.48.wav"), "Relative path line updated");
        AssertTrue(text.Contains("#wav003 other/000201_하트 지상_tempo1.48.wav"), "Filename match keeps original '/' separator");
        AssertTrue(text.Contains("#WAV002 bgm.wav"), "Unrelated line untouched");
        AssertTrue(File.Exists(bms + ".bak"), "Backup created when lines changed");
    }

    private static void TestBmsPreservesBomAndLineEndings(string dir)
    {
        string bms = Path.Combine(dir, "a.bms");
        File.WriteAllText(bms, "#TITLE t\r\n#WAV01\ta_dt1.wav  \r\n#BPM 120\r\n", new UTF8Encoding(true));

        BmsSyncService.SyncBmsFile(bms, [new RenamedPath(Path.Combine(dir, "a_dt1.wav"), Path.Combine(dir, "a_dt2.wav"))], out _);

        byte[] bytes = File.ReadAllBytes(bms);
        AssertTrue(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 BOM preserved");
        AssertEqual(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "#TITLE t\r\n#WAV01\ta_dt2.wav\r\n#BPM 120\r\n",
            "CRLF and tab separator preserved");
    }

    private static void TestBmsNoChangeNoBackup(string dir)
    {
        string bms = Path.Combine(dir, "a.bms");
        File.WriteAllText(bms, "#WAV01 keep.wav\n");
        int synced = BmsSyncService.SyncBmsFile(bms, [new RenamedPath(Path.Combine(dir, "x.wav"), Path.Combine(dir, "y.wav"))], out _);
        AssertEqual(synced.ToString(), "0", "No matching lines");
        AssertTrue(!File.Exists(bms + ".bak"), "No backup overwrite when nothing changed");
    }

    private static void TestBmsPrefixDirectoryIsNotParent(string dir)
    {
        // "chart" 폴더의 BMS 기준으로 "chart2\a.wav" 는 하위 경로가 아님 → 상대경로 매핑 금지, 파일명 매칭만
        string chartDir = Path.Combine(dir, "chart");
        Directory.CreateDirectory(chartDir);
        string bms = Path.Combine(chartDir, "a.bms");
        File.WriteAllText(bms, "#WAV01 2\\a.wav\n");

        BmsSyncService.SyncBmsFile(bms, [new RenamedPath(Path.Combine(dir, "chart2", "a.wav"), Path.Combine(dir, "chart2", "b.wav"))], out _);
        AssertEqual(File.ReadAllText(bms), "#WAV01 2\\b.wav\n", "Sibling prefix folder handled by filename match only");
    }

    private static void TestGuiApplySyncsBmsAndUndoRestores(string root)
    {
        string scene = Path.Combine(root, "1번 씬 wav폴더");
        Directory.CreateDirectory(scene);
        File.WriteAllBytes(Path.Combine(scene, "000201_하트_dt1.48.wav"), [1]);
        File.WriteAllBytes(Path.Combine(scene, "000202_하트_dt1.48.wav"), [2]);
        string bms = Path.Combine(root, "hwa2.bms");
        string original = "#TITLE t\n#WAV01 1번 씬 wav폴더\\000201_하트_dt1.48.wav\n#WAV02 1번 씬 wav폴더\\000202_하트_dt1.48.wav\n";
        File.WriteAllText(bms, original, new UTF8Encoding(true));
        byte[] originalBytes = File.ReadAllBytes(bms);

        var vm = new MainViewModel();
        vm.AddPaths([root]);
        AssertEqual(vm.DetectedBmsPath ?? "", bms, "GUI auto-detects BMS in dropped folder");

        vm.SearchText = "_dt1.48";
        vm.ReplaceText = "_dt1.50";
        AssertEqual(vm.RenamedCount.ToString(), "2", "Preview counts 2 renames");

        vm.ApplyRenameAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(Path.Combine(scene, "000201_하트_dt1.50.wav")), "GUI renamed file on disk");
        string after = File.ReadAllText(bms);
        // 회귀 방지: 이전 구현은 적용 직후 OriginalName을 덮어써 BMS 동기화가 0행이었다.
        AssertTrue(after.Contains("000201_하트_dt1.50.wav") && after.Contains("000202_하트_dt1.50.wav"),
            "GUI apply syncs BMS #WAV lines for successful renames");

        vm.UndoRenameAsync().GetAwaiter().GetResult();
        AssertTrue(File.Exists(Path.Combine(scene, "000201_하트_dt1.48.wav")), "Undo restores file name");
        AssertTrue(File.ReadAllBytes(bms).AsSpan().SequenceEqual(originalBytes), "Undo restores BMS bytes exactly (BOM kept)");
        AssertEqual(vm.AllItems[0].OriginalName.Contains("dt1.48").ToString(), "True", "Undo updates list item names");
    }

    private static void TestDeselectClearsConflict(string root)
    {
        File.WriteAllBytes(Path.Combine(root, "a1.wav"), [1]);
        File.WriteAllBytes(Path.Combine(root, "a2.wav"), [2]);

        var vm = new MainViewModel();
        vm.AddPaths([root]);
        vm.UseRegex = true;
        vm.SearchText = @"a\d";
        vm.ReplaceText = "same";
        AssertTrue(vm.AllItems.All(x => x.HasConflict), "Two items to same target are conflicts");

        vm.AllItems[1].IsSelected = false;
        AssertTrue(!vm.AllItems[0].HasConflict, "Deselecting one side clears the conflict");
    }

    private static void WithTempDir(Action<string> test)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"prtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try { test(dir); }
        finally { try { Directory.Delete(dir, true); } catch { /* 임시 폴더 */ } }
    }

    private static void AssertTrue(bool condition, string testName)
        => AssertEqual(condition ? "true" : "false", "true", testName);

    private static void AssertThrows<TException>(Action action, string testName) where TException : Exception
    {
        try { action(); }
        catch (TException) { AssertTrue(true, testName); return; }
        AssertTrue(false, testName);
    }

    private static void AssertEqual(string actual, string expected, string testName)
    {
        if (actual != expected)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {testName} - Expected: '{expected}', Got: '{actual}'");
            Console.ResetColor();
            Environment.Exit(1);
        }

        _passed++;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PASS] {testName}");
        Console.ResetColor();
    }
}
