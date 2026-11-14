using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using PowerRenameAvalonia.Mcp;
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
        WithTempDir(TestBatchChainAndSwapRename);

        Console.WriteLine("\n=== BmsSyncService ===");
        WithTempDir(TestBmsSyncRelativeAndName);
        WithTempDir(TestBmsPreservesBomAndLineEndings);
        WithTempDir(TestBmsNoChangeNoBackup);
        WithTempDir(TestBmsPrefixDirectoryIsNotParent);
        WithTempDir(TestBmsSameFileNameInDifferentSceneFolders);
        WithTempDir(TestBmsFolderRenameUpdatesRelativePaths);

        Console.WriteLine("\n=== MainViewModel (GUI 흐름) ===");
        WithTempDir(TestGuiApplySyncsBmsAndUndoRestores);
        WithTempDir(TestDeselectClearsConflict);
        WithTempDir(TestConflictWithUnmodifiedItem);
        WithTempDir(TestSelectAllAfterPartialDeselect);
        WithTempDir(TestInclusionTogglesLiveSync);
        WithTempDir(TestRemoveNonWavAndClearAll);

        Console.WriteLine("\n=== McpServer & McpTools (E2E) ===");
        WithTempDir(TestMcpEndToEnd);

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

        // 사용할 수 없는 파일명 문자 및 빈 이름 거부
        foreach (var badChar in new[] { "/", ":", "*" })
        {
            AssertEqual(RenameEngine.Transform("kick.wav", false, new RenameOptions("kick", $"bad{badChar}name"), out var charErr),
                "kick.wav", $"Invalid filename char '{badChar}' keeps original");
            AssertTrue(!string.IsNullOrEmpty(charErr), $"Invalid filename char '{badChar}' reports error");
        }
        AssertEqual(RenameEngine.Transform("kick.wav", false, new RenameOptions("kick.wav", "   "), out var emptyErr),
            "kick.wav", "Blank filename keeps original");
        AssertTrue(!string.IsNullOrEmpty(emptyErr), "Blank filename reports error");
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

    private static void TestBatchChainAndSwapRename(string dir)
    {
        string p1 = Path.Combine(dir, "1.wav");
        string p2 = Path.Combine(dir, "2.wav");
        string p3 = Path.Combine(dir, "3.wav");
        File.WriteAllText(p1, "one");
        File.WriteAllText(p2, "two");

        // 연쇄 리네임: 1.wav -> 2.wav, 2.wav -> 3.wav
        var chainResults = FileRenamer.MoveBatch([
            new RenamedPath(p1, p2),
            new RenamedPath(p2, p3)
        ]);
        AssertTrue(chainResults.All(r => r.Error == null), "Batch chain rename has no errors");
        AssertEqual(File.ReadAllText(p2), "one", "Chain moved 1.wav -> 2.wav");
        AssertEqual(File.ReadAllText(p3), "two", "Chain moved 2.wav -> 3.wav");

        // 맞교환 리네임: 2.wav <-> 3.wav
        var swapResults = FileRenamer.MoveBatch([
            new RenamedPath(p2, p3),
            new RenamedPath(p3, p2)
        ]);
        AssertTrue(swapResults.All(r => r.Error == null), "Batch swap rename has no errors");
        AssertEqual(File.ReadAllText(p2), "two", "Swap restored 2.wav content");
        AssertEqual(File.ReadAllText(p3), "one", "Swap restored 3.wav content");
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

    private static void TestBmsSameFileNameInDifferentSceneFolders(string dir)
    {
        string s1 = Path.Combine(dir, "1번 씬");
        string s2 = Path.Combine(dir, "2번 씬");
        Directory.CreateDirectory(s1);
        Directory.CreateDirectory(s2);

        string s1File = Path.Combine(s1, "same.wav");
        string s2Old = Path.Combine(s2, "same.wav");
        string s2New = Path.Combine(s2, "same_renamed.wav");
        File.WriteAllBytes(s1File, [1]);
        File.WriteAllBytes(s2New, [2]); // 2번 씬만 디스크에서 리네임 완료된 상태

        string bms = Path.Combine(dir, "hwa2.bms");
        File.WriteAllText(bms, "#WAV01 1번 씬\\same.wav\n#WAV02 2번 씬\\same.wav\n", new UTF8Encoding(false));

        int synced = BmsSyncService.SyncBmsFile(bms, [new RenamedPath(s2Old, s2New)], out var err);
        AssertEqual(err ?? "", "", "Same-name multi-scene BMS sync has no error");
        AssertEqual(synced.ToString(), "1", "Only renamed scene line updated");

        string after = File.ReadAllText(bms);
        AssertTrue(after.Contains("#WAV01 1번 씬\\same.wav"), "Untouched scene line stays unchanged");
        AssertTrue(after.Contains("#WAV02 2번 씬\\same_renamed.wav"), "Renamed scene line updated");
    }

    private static void TestBmsFolderRenameUpdatesRelativePaths(string dir)
    {
        string oldFolder = Path.Combine(dir, "1번 씬");
        string newFolder = Path.Combine(dir, "1번 씬_v2");
        Directory.CreateDirectory(newFolder);
        File.WriteAllBytes(Path.Combine(newFolder, "a.wav"), [1]);

        string bms = Path.Combine(dir, "hwa2.bms");
        File.WriteAllText(bms, "#WAV01 1번 씬\\a.wav\n#WAV02 1번 씬/b.wav\n", new UTF8Encoding(false));

        int synced = BmsSyncService.SyncBmsFile(bms, [new RenamedPath(oldFolder, newFolder, IsDirectory: true)], out var err);
        AssertEqual(err ?? "", "", "Folder rename BMS sync has no error");
        AssertEqual(synced.ToString(), "2", "Folder rename updates both backslash and slash BMS lines");

        string after = File.ReadAllText(bms);
        AssertTrue(after.Contains("#WAV01 1번 씬_v2\\a.wav"), "Folder rename preserves backslash separator");
        AssertTrue(after.Contains("#WAV02 1번 씬_v2/b.wav"), "Folder rename preserves forward slash separator");
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

    private static void TestConflictWithUnmodifiedItem(string root)
    {
        File.WriteAllBytes(Path.Combine(root, "a1.wav"), [1]);
        File.WriteAllBytes(Path.Combine(root, "a2.wav"), [2]);

        var vm = new MainViewModel();
        vm.AddPaths([root]);
        vm.SearchText = "a1";
        vm.ReplaceText = "a2";

        var a1 = vm.AllItems.Single(x => x.OriginalName == "a1.wav");
        var a2 = vm.AllItems.Single(x => x.OriginalName == "a2.wav");
        AssertTrue(a1.HasConflict, "Renaming into unmodified file triggers conflict");
        AssertTrue(!a2.HasConflict, "Unmodified target file itself is not marked as conflict");
    }

    private static void TestSelectAllAfterPartialDeselect(string root)
    {
        File.WriteAllBytes(Path.Combine(root, "k1.wav"), [1]);
        File.WriteAllBytes(Path.Combine(root, "k2.wav"), [2]);

        var vm = new MainViewModel();
        vm.AddPaths([root]);
        AssertTrue(vm.IsAllSelected, "IsAllSelected starts true when all items selected");

        vm.AllItems[0].IsSelected = false;
        AssertTrue(!vm.IsAllSelected, "Partial deselect updates IsAllSelected to false");

        vm.SelectAll();
        AssertTrue(vm.AllItems.All(x => x.IsSelected), "SelectAll re-selects all items after partial deselect");
        AssertTrue(vm.IsAllSelected, "IsAllSelected returns to true after SelectAll");
    }

    private static void TestInclusionTogglesLiveSync(string root)
    {
        string sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(root, "top.wav"), [1]);
        File.WriteAllBytes(Path.Combine(root, "cover.png"), [2]);
        File.WriteAllBytes(Path.Combine(sub, "deep.wav"), [3]);

        var vm = new MainViewModel();
        vm.AddPaths([root]);
        AssertEqual(vm.OriginalCount.ToString(), "2", "Default options load 2 WAV files");

        vm.OnlyWavFiles = false;
        AssertEqual(vm.OriginalCount.ToString(), "3", "Disabling OnlyWavFiles discovers non-WAV files live");
        AssertTrue(vm.AllItems.All(x => x.IsSelected), "All 3 files selected when OnlyWavFiles is false");

        vm.OnlyWavFiles = true;
        AssertTrue(!vm.AllItems.Single(x => x.OriginalName == "cover.png").IsSelected, "Enabling OnlyWavFiles unselects cover.png");

        vm.IncludeFolders = true;
        AssertTrue(vm.AllItems.Any(x => x.IsDirectory && x.OriginalName == "sub"), "Enabling IncludeFolders adds subdirectories live");

        vm.IncludeSubfolders = false;
        AssertTrue(!vm.AllItems.Single(x => x.OriginalName == "deep.wav").IsSelected, "Disabling IncludeSubfolders unselects nested files");
        AssertTrue(vm.AllItems.Single(x => x.OriginalName == "top.wav").IsSelected, "Top-level WAV stays selected");
    }

    private static void TestRemoveNonWavAndClearAll(string root)
    {
        File.WriteAllBytes(Path.Combine(root, "a.wav"), [1]);
        File.WriteAllBytes(Path.Combine(root, "b.png"), [2]);

        var vm = new MainViewModel { OnlyWavFiles = false };
        vm.AddPaths([root]);
        AssertEqual(vm.OriginalCount.ToString(), "2", "Loaded wav and png");

        vm.RemoveNonWavFiles();
        AssertEqual(vm.OriginalCount.ToString(), "1", "RemoveNonWavFiles leaves only wav");
        AssertEqual(vm.AllItems[0].OriginalName, "a.wav", "Remaining item is wav");

        vm.ClearAll();
        AssertEqual(vm.OriginalCount.ToString(), "0", "ClearAll empties AllItems");
        AssertEqual(vm.DisplayItems.Count.ToString(), "0", "ClearAll empties DisplayItems");
        AssertTrue(!vm.IsAllSelected, "ClearAll updates IsAllSelected to false");
    }

    private static void TestMcpEndToEnd(string root)
    {
        string s1 = Path.Combine(root, "1번 씬");
        string s2 = Path.Combine(root, "2번 씬");
        Directory.CreateDirectory(s1);
        Directory.CreateDirectory(s2);
        File.WriteAllBytes(Path.Combine(s1, "kick_dt1.48.wav"), [1]);
        File.WriteAllBytes(Path.Combine(s2, "snare_dt1.48.wav"), [2]);
        string bms = Path.Combine(root, "hwa2.bms");
        File.WriteAllText(bms, "#WAV01 1번 씬\\kick_dt1.48.wav\n#WAV02 2번 씬\\snare_dt1.48.wav\n", new UTF8Encoding(false));

        static List<JsonObject> CallMcp(params JsonObject[] requests)
        {
            string payload = string.Join("\n", requests.Select(r => r.ToJsonString())) + "\n";
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            using var output = new MemoryStream();
            var server = new McpServer(input, output, TextWriter.Null);
            server.RunAsync().GetAwaiter().GetResult();

            output.Position = 0;
            using var reader = new StreamReader(output, new UTF8Encoding(false));
            var responses = new List<JsonObject>();
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    responses.Add(JsonNode.Parse(line)!.AsObject());
            }
            return responses;
        }

        static JsonObject ToolCall(int id, string name, JsonObject arguments) => new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
        };

        static JsonObject ParseToolText(JsonObject response)
        {
            string text = response["result"]!["content"]![0]!["text"]!.GetValue<string>();
            return JsonNode.Parse(text)!.AsObject();
        }

        var initRes = CallMcp(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "initialize",
            ["params"] = new JsonObject { ["protocolVersion"] = "2025-11-25" }
        }).Single();
        AssertEqual(initRes["result"]!["serverInfo"]!["name"]!.GetValue<string>(), "wav-bms-renamer", "MCP initialize returns server name");

        var listRes = CallMcp(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/list" }).Single();
        AssertEqual(listRes["result"]!["tools"]!.AsArray().Count.ToString(), "4", "MCP tools/list returns 4 tools");

        var scanJson = ParseToolText(CallMcp(ToolCall(3, "scan_wav_assets", new JsonObject { ["directory"] = root })).Single());
        AssertEqual(scanJson["total_wav_count"]!.GetValue<int>().ToString(), "2", "MCP scan_wav_assets finds 2 WAVs");
        AssertEqual(scanJson["missing_wav_files_count"]!.GetValue<int>().ToString(), "0", "MCP scan_wav_assets reports 0 missing before rename");

        var previewJson = ParseToolText(CallMcp(ToolCall(4, "preview_rename", new JsonObject
        {
            ["directory"] = root,
            ["search_text"] = "_dt1.48",
            ["replace_text"] = "_dt1.50"
        })).Single());
        AssertEqual(previewJson["applied_to_disk"]!.GetValue<bool>().ToString(), "False", "MCP preview_rename does not modify disk");
        AssertEqual(previewJson["modified_count"]!.GetValue<int>().ToString(), "2", "MCP preview_rename simulates 2 renames");
        AssertTrue(File.Exists(Path.Combine(s1, "kick_dt1.48.wav")), "Original file remains after MCP preview");

        var applyJson = ParseToolText(CallMcp(ToolCall(5, "apply_rename", new JsonObject
        {
            ["directory"] = root,
            ["search_text"] = "_dt1.48",
            ["replace_text"] = "_dt1.50"
        })).Single());
        AssertEqual(applyJson["success_count"]!.GetValue<int>().ToString(), "2", "MCP apply_rename renames 2 files");
        AssertEqual(applyJson["bms_synced_lines"]!.GetValue<int>().ToString(), "2", "MCP apply_rename syncs 2 BMS lines");
        AssertTrue(File.Exists(Path.Combine(s1, "kick_dt1.50.wav")), "MCP apply_rename created renamed file");

        var syncJson = ParseToolText(CallMcp(ToolCall(6, "sync_bms_chart", new JsonObject
        {
            ["bms_path"] = bms,
            ["replacements"] = new JsonObject { ["2번 씬\\snare_dt1.50.wav"] = "2번 씬\\snare_final.wav" }
        })).Single());
        AssertEqual(syncJson["synced_lines"]!.GetValue<int>().ToString(), "1", "MCP sync_bms_chart updates 1 relative line");
        AssertTrue(File.ReadAllText(bms).Contains("#WAV02 2번 씬\\snare_final.wav"), "MCP sync_bms_chart persisted relative path change");
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
