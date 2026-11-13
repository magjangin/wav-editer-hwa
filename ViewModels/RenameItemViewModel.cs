using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerRenameAvalonia.Models;

namespace PowerRenameAvalonia.ViewModels;

public partial class RenameItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _fullPath;

    [ObservableProperty]
    private string _directoryPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified), nameof(DisplayIcon), nameof(ArrowColor), nameof(NewNameColor), nameof(IsAudioFile), nameof(Extension))]
    private string _originalName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified), nameof(ArrowColor), nameof(NewNameColor))]
    private string _newName;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private RenameStatus _status = RenameStatus.Pending;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewNameColor))]
    private bool _hasConflict;

    // 필터·카운트에서 수천 번 호출되므로 원래 이름이 바뀔 때만 다시 계산한다.
    private string _extension = string.Empty;

    public bool IsModified => !string.Equals(OriginalName, NewName, StringComparison.Ordinal);

    public string Extension => _extension;

    public bool IsWavFile => !IsDirectory && _extension == ".wav";

    public bool IsAudioFile => _extension is ".wav" or ".mp3" or ".ogg" or ".flac" or ".aiff" or ".m4a";

    public string DisplayIcon => IsDirectory ? "📁" : (IsAudioFile ? "🎵" : "📄");

    public string ArrowColor => IsModified ? "#a78bfa" : "#333333";

    public string NewNameColor
    {
        get
        {
            if (HasConflict) return "#f87171"; // Red
            if (IsModified) return "#38bdf8";  // Cyan / Sky
            return "#777777";                 // Dim Gray
        }
    }

    /// <summary>이 항목을 적용했을 때의 대상 전체 경로.</summary>
    public string TargetPath => Path.Combine(DirectoryPath, NewName);

    public RenameItemViewModel(string path, bool isDirectory)
    {
        _fullPath = path;
        _isDirectory = isDirectory;

        if (isDirectory)
        {
            var dirInfo = new DirectoryInfo(path);
            _directoryPath = dirInfo.Parent?.FullName ?? path;
            _originalName = dirInfo.Name;
        }
        else
        {
            var fileInfo = new FileInfo(path);
            _directoryPath = fileInfo.DirectoryName ?? string.Empty;
            _originalName = fileInfo.Name;
        }

        _newName = _originalName;
        _extension = Path.GetExtension(_originalName).ToLowerInvariant();
    }

    partial void OnOriginalNameChanged(string value)
    {
        _extension = Path.GetExtension(value).ToLowerInvariant();
    }

    /// <summary>디스크 이동이 끝난 뒤 항목을 새 경로 기준으로 갱신한다.</summary>
    public void MarkMovedTo(string newPath)
    {
        FullPath = newPath;
        OriginalName = Path.GetFileName(newPath);
        NewName = OriginalName;
    }
}
