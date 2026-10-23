using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerRenameAvalonia.Models;
using PowerRenameAvalonia.Services;

namespace PowerRenameAvalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _useRegex = false;

    [ObservableProperty]
    private bool _matchAll = true;

    [ObservableProperty]
    private bool _matchCase = false;

    [ObservableProperty]
    private RenameScope _selectedScope = RenameScope.NameAndExtension;

    [ObservableProperty]
    private TextFormatting _selectedFormatting = TextFormatting.None;

    [ObservableProperty]
    private bool _includeFiles = true;

    [ObservableProperty]
    private bool _includeFolders = false; // 기본값: 폴더 이름 변경은 제외(안전)

    [ObservableProperty]
    private bool _includeSubfolders = true; // 기본값: 하위 폴더 재귀 탐색 활성화!

    [ObservableProperty]
    private bool _onlyWavFiles = true; // 기본값: WAV 파일 전용 모드!

    [ObservableProperty]
    private bool _syncWithBms = true; // BMS 파일 동기화 옵션!

    [ObservableProperty]
    private string? _detectedBmsPath;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isAllSelected = true;

    [ObservableProperty]
    private string? _globalErrorMessage;

    [ObservableProperty]
    private string _statusMessage = "파일 또는 폴더를 드래그 앤 드롭하거나 추가하세요.";

    public ObservableCollection<RenameItemViewModel> AllItems { get; } = [];
    public ObservableCollection<RenameItemViewModel> DisplayItems { get; } = [];

    // 실행 취소를 위한 히스토리 (OldPath -> NewPath, BMS 백업 경로)
    private readonly Stack<(List<(string from, string to, bool isDirectory)> files, string? bmsPath, string? bmsBackupContent)> _undoHistory = new();

    public int OriginalCount => AllItems.Count;
    public int RenamedCount => AllItems.Count(x => x.IsSelected && x.IsModified);
    public int WavFileCount => AllItems.Count(x => x.IsAudioFile && x.Extension == ".wav");
    public bool CanUndo => _undoHistory.Count > 0;

    public bool HasDetectedBms => !string.IsNullOrEmpty(DetectedBmsPath) && File.Exists(DetectedBmsPath);
    public string DetectedBmsName => string.IsNullOrEmpty(DetectedBmsPath) ? "없음" : Path.GetFileName(DetectedBmsPath);

    public MainViewModel()
    {
        AllItems.CollectionChanged += (s, e) =>
        {
            UpdateCounts();
            FilterList();
        };
    }

    partial void OnSearchTextChanged(string value) => RefreshPreview();
    partial void OnReplaceTextChanged(string value) => RefreshPreview();
    partial void OnUseRegexChanged(bool value) => RefreshPreview();
    partial void OnMatchAllChanged(bool value) => RefreshPreview();
    partial void OnMatchCaseChanged(bool value) => RefreshPreview();
    partial void OnSelectedScopeChanged(RenameScope value) => RefreshPreview();
    partial void OnSelectedFormattingChanged(TextFormatting value) => RefreshPreview();
    partial void OnFilterTextChanged(string value) => FilterList();

    partial void OnDetectedBmsPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasDetectedBms));
        OnPropertyChanged(nameof(DetectedBmsName));
    }

    partial void OnOnlyWavFilesChanged(bool value)
    {
        if (value)
        {
            SelectOnlyWavFiles();
        }
        UpdateCounts();
    }

    partial void OnIsAllSelectedChanged(bool value)
    {
        foreach (var item in AllItems)
        {
            item.IsSelected = value;
        }
        UpdateCounts();
    }

    [RelayCommand]
    public void SetFormatting(string formatName)
    {
        if (Enum.TryParse<TextFormatting>(formatName, out var formatting))
        {
            SelectedFormatting = SelectedFormatting == formatting ? TextFormatting.None : formatting;
        }
    }

    [RelayCommand]
    public void ClearFormatting()
    {
        SelectedFormatting = TextFormatting.None;
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var pathList = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var existingPaths = new HashSet<string>(AllItems.Select(x => x.FullPath), StringComparer.OrdinalIgnoreCase);

        // BMS 파일 자동 탐지
        if (string.IsNullOrEmpty(DetectedBmsPath))
        {
            var detected = BmsSyncService.DetectBmsFile(pathList);
            if (!string.IsNullOrEmpty(detected))
            {
                DetectedBmsPath = detected;
            }
        }

        foreach (var path in pathList)
        {
            if (File.Exists(path))
            {
                // .bms 파일을 직접 드롭한 경우 자동 연동
                if (Path.GetExtension(path).Equals(".bms", StringComparison.OrdinalIgnoreCase))
                {
                    DetectedBmsPath = path;
                    continue;
                }

                if (ShouldIncludeFile(path) && existingPaths.Add(path))
                {
                    var vm = new RenameItemViewModel(path, false);
                    HookItem(vm);
                    AllItems.Add(vm);
                }
            }
            else if (Directory.Exists(path))
            {
                if (IncludeFolders && existingPaths.Add(path))
                {
                    var vm = new RenameItemViewModel(path, true);
                    HookItem(vm);
                    AllItems.Add(vm);
                }

                try
                {
                    var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    if (IncludeFiles)
                    {
                        var pattern = OnlyWavFiles ? "*.wav" : "*";
                        foreach (var subFile in Directory.EnumerateFiles(path, pattern, searchOption))
                        {
                            if (ShouldIncludeFile(subFile) && existingPaths.Add(subFile))
                            {
                                var vm = new RenameItemViewModel(subFile, false);
                                HookItem(vm);
                                AllItems.Add(vm);
                            }
                        }
                    }

                    if (IncludeFolders && IncludeSubfolders)
                    {
                        foreach (var subDir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                        {
                            if (existingPaths.Add(subDir))
                            {
                                var vm = new RenameItemViewModel(subDir, true);
                                HookItem(vm);
                                AllItems.Add(vm);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"폴더 탐색 중 오류: {ex.Message}";
                }
            }
        }

        RefreshPreview();
    }

    private bool ShouldIncludeFile(string filePath)
    {
        if (!IncludeFiles) return false;
        if (OnlyWavFiles)
        {
            return Path.GetExtension(filePath).Equals(".wav", StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private void HookItem(RenameItemViewModel item)
    {
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RenameItemViewModel.IsSelected))
            {
                UpdateCounts();
            }
        };
    }

    [RelayCommand]
    public void SelectOnlyWavFiles()
    {
        foreach (var item in AllItems)
        {
            item.IsSelected = !item.IsDirectory && item.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
        }
        UpdateCounts();
    }

    [RelayCommand]
    public void RemoveNonWavFiles()
    {
        var nonWav = AllItems.Where(x => x.IsDirectory || !x.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var item in nonWav)
        {
            AllItems.Remove(item);
            DisplayItems.Remove(item);
        }
        RefreshPreview();
        StatusMessage = $"{nonWav.Count}개의 비-WAV 항목을 목록에서 제거했습니다.";
    }

    [RelayCommand]
    public void RemoveItem(RenameItemViewModel item)
    {
        AllItems.Remove(item);
        DisplayItems.Remove(item);
        RefreshPreview();
    }

    [RelayCommand]
    public void ClearAll()
    {
        AllItems.Clear();
        DisplayItems.Clear();
        UpdateCounts();
    }

    [RelayCommand]
    public void ClearBms()
    {
        DetectedBmsPath = null;
    }

    [RelayCommand]
    public void SelectAll()
    {
        IsAllSelected = true;
    }

    [RelayCommand]
    public void DeselectAll()
    {
        IsAllSelected = false;
    }

    public void RefreshPreview()
    {
        GlobalErrorMessage = null;
        string? firstError = null;

        foreach (var item in AllItems)
        {
            string newName = RenameEngine.Transform(
                item.OriginalName,
                item.IsDirectory,
                SearchText,
                ReplaceText,
                UseRegex,
                MatchAll,
                MatchCase,
                SelectedScope,
                SelectedFormatting,
                out var error
            );

            if (error != null && firstError == null)
            {
                firstError = error;
            }

            item.NewName = newName;
            item.Status = RenameStatus.Pending;
            item.StatusMessage = null;
            item.HasConflict = false;
        }

        GlobalErrorMessage = firstError;

        CheckConflicts();
        UpdateCounts();
    }

    private void CheckConflicts()
    {
        var groups = AllItems
            .Where(x => x.IsSelected && x.IsModified)
            .GroupBy(x => Path.Combine(x.DirectoryPath, x.NewName).ToLowerInvariant());

        foreach (var group in groups)
        {
            if (group.Count() > 1)
            {
                foreach (var item in group)
                {
                    item.HasConflict = true;
                    item.Status = RenameStatus.Conflict;
                    item.StatusMessage = "중복된 대상 파일명이 존재합니다.";
                }
            }
        }
    }

    public void FilterList()
    {
        DisplayItems.Clear();
        var filter = FilterText.Trim();

        foreach (var item in AllItems)
        {
            if (string.IsNullOrEmpty(filter) ||
                item.OriginalName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.NewName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                DisplayItems.Add(item);
            }
        }
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(OriginalCount));
        OnPropertyChanged(nameof(RenamedCount));
        OnPropertyChanged(nameof(WavFileCount));
    }

    [RelayCommand]
    public async Task ApplyRenameAsync()
    {
        var targetItems = AllItems.Where(x => x.IsSelected && x.IsModified && !x.HasConflict).ToList();
        if (targetItems.Count == 0)
        {
            StatusMessage = "이름을 바꿀 선택된 항목이 없습니다.";
            return;
        }

        int successCount = 0;
        int errorCount = 0;
        var batchHistory = new List<(string from, string to, bool isDirectory)>();

        // BMS 파일 백업 텍스트 보관 (Undo 용)
        string? bmsBackupContent = null;
        if (SyncWithBms && HasDetectedBms && File.Exists(DetectedBmsPath))
        {
            try
            {
                bmsBackupContent = File.ReadAllText(DetectedBmsPath);
            }
            catch { }
        }

        var sortedItems = targetItems
            .OrderByDescending(x => x.FullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
            .ToList();

        foreach (var item in sortedItems)
        {
            try
            {
                string oldPath = item.FullPath;
                string newPath = Path.Combine(item.DirectoryPath, item.NewName);

                if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
                    continue;

                if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    string tempPath = Path.Combine(item.DirectoryPath, $"__pr_temp_{Guid.NewGuid():N}");
                    if (item.IsDirectory)
                    {
                        Directory.Move(oldPath, tempPath);
                        Directory.Move(tempPath, newPath);
                    }
                    else
                    {
                        File.Move(oldPath, tempPath);
                        File.Move(tempPath, newPath);
                    }
                }
                else
                {
                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(newPath))
                            throw new IOException($"이미 대상 폴더가 존재합니다: {item.NewName}");
                        Directory.Move(oldPath, newPath);
                    }
                    else
                    {
                        if (File.Exists(newPath))
                            throw new IOException($"이미 대상 파일이 존재합니다: {item.NewName}");
                        File.Move(oldPath, newPath);
                    }
                }

                batchHistory.Add((oldPath, newPath, item.IsDirectory));
                item.FullPath = newPath;
                item.OriginalName = item.NewName;
                item.Status = RenameStatus.Renamed;
                item.StatusMessage = "완료";
                successCount++;
            }
            catch (Exception ex)
            {
                item.Status = RenameStatus.Error;
                item.StatusMessage = ex.Message;
                errorCount++;
            }
        }

        // BMS 파일 동기화 갱신
        int bmsSyncCount = 0;
        string? bmsError = null;
        if (SyncWithBms && HasDetectedBms && !string.IsNullOrEmpty(DetectedBmsPath))
        {
            bmsSyncCount = BmsSyncService.SyncBmsFile(DetectedBmsPath, targetItems, out bmsError);
        }

        if (batchHistory.Count > 0)
        {
            _undoHistory.Push((batchHistory, DetectedBmsPath, bmsBackupContent));
            OnPropertyChanged(nameof(CanUndo));
        }

        string bmsMsg = HasDetectedBms && SyncWithBms ? $" (BMS 동기화: {bmsSyncCount}개 행 갱신)" : "";
        StatusMessage = $"완료: {successCount}개 변경 성공{bmsMsg}" + (errorCount > 0 ? $", {errorCount}개 실패" : "") + (bmsError != null ? $" [BMS 오류: {bmsError}]" : "");
        RefreshPreview();
        await Task.CompletedTask;
    }

    [RelayCommand]
    public void UndoRename()
    {
        if (_undoHistory.Count == 0) return;

        var (files, bmsPath, bmsBackupContent) = _undoHistory.Pop();
        int undoSuccess = 0;

        foreach (var (from, to, isDirectory) in files.AsEnumerable().Reverse())
        {
            try
            {
                if (isDirectory)
                {
                    if (Directory.Exists(to))
                    {
                        Directory.Move(to, from);
                        undoSuccess++;
                    }
                }
                else
                {
                    if (File.Exists(to))
                    {
                        File.Move(to, from);
                        undoSuccess++;
                    }
                }

                var item = AllItems.FirstOrDefault(x => x.FullPath.Equals(to, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.FullPath = from;
                    item.OriginalName = Path.GetFileName(from);
                    item.NewName = item.OriginalName;
                }
            }
            catch
            {
                // ignore
            }
        }

        // BMS 파일 원복
        if (!string.IsNullOrEmpty(bmsPath) && bmsBackupContent != null && File.Exists(bmsPath))
        {
            try
            {
                File.WriteAllText(bmsPath, bmsBackupContent);
            }
            catch { }
        }

        OnPropertyChanged(nameof(CanUndo));
        StatusMessage = $"실행 취소 완료: {undoSuccess}개 파일 및 BMS 파일 복구됨";
        RefreshPreview();
    }
}
