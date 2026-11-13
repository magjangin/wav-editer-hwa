using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using PowerRenameAvalonia.ViewModels;

namespace PowerRenameAvalonia.Tests;

/// <summary>
/// <c>dotnet run -c Release -- --bench [개수]</c>: 대량 WAV 목록에서 ViewModel 병목을 측정한다.
/// UI 바인딩을 흉내 내기 위해 속성 변경 시 값을 다시 읽고, 목록 변경 이벤트 수를 센다.
/// </summary>
public static class Benchmark
{
    public static void Run(int count)
    {
        string root = Path.Combine(Path.GetTempPath(), $"prbench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < count; i++)
                File.WriteAllBytes(Path.Combine(root, $"{i:D6}_노트_dt1.48.wav"), [1]);

            var vm = new MainViewModel();
            long events = 0;
            NotifyCollectionChangedEventHandler onListChanged = (_, _) => events++;
            vm.DisplayItems.CollectionChanged += onListChanged;
            vm.PropertyChanged += (_, e) =>
            {
                events++;
                var value = typeof(MainViewModel).GetProperty(e.PropertyName!)?.GetValue(vm);
                if (e.PropertyName == nameof(MainViewModel.DisplayItems) && value is INotifyCollectionChanged list)
                    list.CollectionChanged += onListChanged;
            };

            var sw = Stopwatch.StartNew();
            vm.AddPaths([root]);
            Console.WriteLine($"AddPaths({count}): {sw.ElapsedMilliseconds} ms, 알림 {events:N0}건");

            events = 0;
            sw.Restart();
            vm.UseRegex = true;
            foreach (var s in new[] { "_", "_d", "_dt", @"_dt(\d)", @"_dt(\d)\.", @"_dt(\d)\.(\d+)" }) vm.SearchText = s;
            Console.WriteLine($"정규식 키 입력 6회 미리보기: {sw.ElapsedMilliseconds} ms");

            events = 0;
            sw.Restart();
            vm.IsAllSelected = false;
            vm.IsAllSelected = true;
            Console.WriteLine($"전체 선택 토글 2회: {sw.ElapsedMilliseconds} ms, 알림 {events:N0}건");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
