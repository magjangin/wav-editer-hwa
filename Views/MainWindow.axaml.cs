using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PowerRenameAvalonia.ViewModels;

namespace PowerRenameAvalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private static List<string> ToLocalPaths(IEnumerable<IStorageItem>? items)
        => items?
            .Select(item => item.TryGetLocalPath())
            .OfType<string>()
            .Where(path => path.Length > 0)
            .ToList() ?? [];

    private void AddItems(IEnumerable<IStorageItem>? items)
    {
        var paths = ToLocalPaths(items);
        if (paths.Count > 0) ViewModel?.AddPaths(paths);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = files is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e) => AddItems(e.DataTransfer.TryGetFiles());

    private async void OnAddFilesClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        AddItems(await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "이름을 바꿀 파일 선택",
            AllowMultiple = true
        }));
    }

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        AddItems(await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "이름을 바꿀 폴더 선택",
            AllowMultiple = true
        }));
    }

    private async void OnSelectBmsClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "연동할 BMS 차트 파일 선택",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("BMS 차트 파일 (*.bms)") { Patterns = ["*.bms"] },
                new FilePickerFileType("모든 파일 (*.*)") { Patterns = ["*.*"] }
            ]
        });

        var path = ToLocalPaths(files).FirstOrDefault();
        if (path != null) vm.DetectedBmsPath = path;
    }
}
