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
    private string _originalName;

    [ObservableProperty]
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
    private bool _hasConflict;

    public bool IsModified => OriginalName != NewName;

    public string Extension => Path.GetExtension(OriginalName).ToLowerInvariant();

    public bool IsAudioFile => Extension is ".wav" or ".mp3" or ".ogg" or ".flac" or ".aiff" or ".m4a";

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
    }

    partial void OnNewNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(ArrowColor));
        OnPropertyChanged(nameof(NewNameColor));
    }

    partial void OnOriginalNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(DisplayIcon));
        OnPropertyChanged(nameof(ArrowColor));
        OnPropertyChanged(nameof(NewNameColor));
    }

    partial void OnHasConflictChanged(bool value)
    {
        OnPropertyChanged(nameof(NewNameColor));
    }
}
