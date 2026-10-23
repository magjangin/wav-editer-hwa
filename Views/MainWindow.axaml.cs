using System;
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files != null && files.Length > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null || files.Length == 0) return;

        var paths = new List<string>();
        foreach (var file in files)
        {
            var localPath = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                paths.Add(localPath);
            }
        }

        if (paths.Count > 0 && DataContext is MainViewModel vm)
        {
            vm.AddPaths(paths);
        }
    }

    private async void OnAddFilesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "이름을 바꿀 파일 선택",
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = files
                .Select(f => f.TryGetLocalPath())
                .OfType<string>()
                .Where(p => !string.IsNullOrEmpty(p));
            vm.AddPaths(paths);
        }
    }

    private async void OnAddFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "이름을 바꿀 폴더 선택",
            AllowMultiple = true
        });

        if (folders.Count > 0)
        {
            var paths = folders
                .Select(f => f.TryGetLocalPath())
                .OfType<string>()
                .Where(p => !string.IsNullOrEmpty(p));
            vm.AddPaths(paths);
        }
    }

    private async void OnSelectBmsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "연동할 BMS 차트 파일 선택",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("BMS 차트 파일 (*.bms)")
                {
                    Patterns = new[] { "*.bms" }
                },
                new FilePickerFileType("모든 파일 (*.*)")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                vm.DetectedBmsPath = path;
            }
        }
    }
}
