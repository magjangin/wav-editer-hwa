using Avalonia;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using PowerRenameAvalonia.Mcp;
using PowerRenameAvalonia.Models;
using PowerRenameAvalonia.Services;
using PowerRenameAvalonia.ViewModels;

namespace PowerRenameAvalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test")
        {
            RunSelfTests();
            return;
        }

        if (args.Length > 0 && args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
        {
            RunMcpServerAsync().GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static async Task RunMcpServerAsync()
    {
        var server = new McpServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
        await server.RunAsync();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void RunSelfTests()
    {
        Console.WriteLine("=== Running PowerRename Engine Self-Tests ===");

        // Test 1: Simple string replace in NameAndExtension
        string res1 = RenameEngine.Transform(
            "000201_하트 지상_dt1.48.wav",
            false,
            "_dt",
            "_tempo",
            false,
            true,
            false,
            RenameScope.NameAndExtension,
            TextFormatting.None,
            out var err1
        );
        AssertEqual(res1, "000201_하트 지상_tempo1.48.wav", "Test 1: Simple replace");

        // Test 2: Regex replace with capture group
        string res2 = RenameEngine.Transform(
            "000201_하트 지상_dt1.48.wav",
            false,
            @"_dt(\d+\.\d+)",
            "_speed$1",
            true,
            true,
            false,
            RenameScope.NameAndExtension,
            TextFormatting.None,
            out var err2
        );
        AssertEqual(res2, "000201_하트 지상_speed1.48.wav", "Test 2: Regex capture group");

        // Test 3: NameOnly scope - extension preserved
        string res3 = RenameEngine.Transform(
            "MyAudioFile.WAV",
            false,
            "Audio",
            "Sound",
            false,
            true,
            false,
            RenameScope.NameOnly,
            TextFormatting.Lowercase,
            out var err3
        );
        AssertEqual(res3, "mysoundfile.WAV", "Test 3: NameOnly scope + Lowercase formatting");

        // Test 4: ExtensionOnly scope
        string res4 = RenameEngine.Transform(
            "sample.wav",
            false,
            "wav",
            "ogg",
            false,
            true,
            false,
            RenameScope.ExtensionOnly,
            TextFormatting.None,
            out var err4
        );
        AssertEqual(res4, "sample.ogg", "Test 4: ExtensionOnly scope");

        // Test 5: First match only (MatchAll = false)
        string res5 = RenameEngine.Transform(
            "abc_abc_abc.wav",
            false,
            "abc",
            "xyz",
            false,
            false,
            false,
            RenameScope.NameAndExtension,
            TextFormatting.None,
            out var err5
        );
        AssertEqual(res5, "xyz_abc_abc.wav", "Test 5: Match first occurrence only");

        // Test 6: BMS #WAV Sync Test
        Console.WriteLine("\n=== Running BMS Sync Self-Test ===");
        string tempBms = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.bms");
        string bmsContent = "#TITLE test\n#WAV001 10번 씬 wav폴더\\000201_하트 지상_dt1.48.wav\n#WAV002 bgm.wav\n";
        File.WriteAllText(tempBms, bmsContent);

        try
        {
            var item = new RenameItemViewModel(Path.Combine(Path.GetTempPath(), "10번 씬 wav폴더", "000201_하트 지상_dt1.48.wav"), false)
            {
                NewName = "000201_하트 지상_tempo1.48.wav"
            };

            int synced = BmsSyncService.SyncBmsFile(tempBms, new[] { item }, out var bmsErr);
            AssertEqual(synced.ToString(), "1", "Test 6: BMS Synced 1 item");

            string updatedBms = File.ReadAllText(tempBms);
            bool containsNew = updatedBms.Contains("000201_하트 지상_tempo1.48.wav");
            AssertEqual(containsNew ? "true" : "false", "true", "Test 6: BMS Content contains new wav name");
        }
        finally
        {
            File.Delete(tempBms);
            string bak = tempBms + ".bak";
            if (File.Exists(bak)) File.Delete(bak);
        }

        Console.WriteLine("\nAll tests passed successfully!");
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
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[PASS] {testName} -> '{actual}'");
            Console.ResetColor();
        }
    }
}
