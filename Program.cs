using System;
using Avalonia;
using PowerRenameAvalonia.Mcp;
using PowerRenameAvalonia.Tests;

namespace PowerRenameAvalonia;

sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : string.Empty;

        switch (mode.ToLowerInvariant())
        {
            case "--test":
                return SelfTests.Run();

            case "--bench":
                Benchmark.Run(args.Length > 1 && int.TryParse(args[1], out var count) ? count : 2000);
                return 0;

            case "mcp":
                var server = new McpServer(Console.OpenStandardInput(), Console.OpenStandardOutput(), Console.Error);
                return server.RunAsync().GetAwaiter().GetResult();

            default:
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
