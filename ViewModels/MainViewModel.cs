using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private bool _includeSubfolders = true; // 기본값: 하위 폴더 재귀 탐색 활성화

    [ObservableProperty]
    private bool _onlyWavFiles = true; // 기본값: WAV 파일 전용 모드

    [ObservableProperty]
    private bool _syncWithBms = true; // BMS 파일 동기화 옵션

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedBms), nameof(DetectedBmsName))]
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

    /// <summary>
    /// 필터 결과. 항목을 하나씩 Clear/Add 하면 목록 컨트롤에 N개의 변경 이벤트가 가므로
    /// 필터가 바뀔 때마다 컬렉션 자체를 교체해 이벤트를 1회로 줄인다.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<RenameItemViewModel> _displayItems = [];

    private readonly Stack<UndoEntry> _undoHistory = new();

    // 대량 추가/선택 변경 중에는 항목별 재계산을 막고 끝에서 한 번만 수행한다.
    private bool _isBatchUpdating;

    private sealed record UndoEntry(List<RenamedPath> Files, string? BmsPath, byte[]? BmsBackup);

    public int OriginalCount => AllItems.Count;
    public int RenamedCount => AllItems.Count(x => x.IsSelected && x.IsModified);
    public int WavFileCount => AllItems.Count(x => x.IsWavFile);
    public bool CanUndo => _undoHistory.Count > 0;

    public bool HasDetectedBms => !string.IsNullOrEmpty(DetectedBmsPath) && File.Exists(DetectedBmsPath);
    public string DetectedBmsName => string.IsNullOrEmpty(DetectedBmsPath) ? "없음" : Path.GetFileName(DetectedBmsPath);

    public MainViewModel()
    {
        AllItems.CollectionChanged += OnAllItemsChanged;
    }

    partial void OnSearchTextChanged(string value) => RefreshPreview();
    partial void OnReplaceTextChanged(string value) => RefreshPreview();
    partial void OnUseRegexChanged(bool value) => RefreshPreview();
    partial void OnMatchAllChanged(bool value) => RefreshPreview();
    partial void OnMatchCaseChanged(bool value) => RefreshPreview();
    partial void OnSelectedScopeChanged(RenameScope value) => RefreshPreview();
    partial void OnSelectedFormattingChanged(TextFormatting value) => RefreshPreview();
    partial void OnFilterTextChanged(string value) => FilterList();

    partial void OnOnlyWavFilesChanged(bool value)
    {
        if (value) SelectOnlyWavFiles();
    }

    partial void OnIsAllSelectedChanged(bool value)
    {
        RunBatch(() =>
        {
            foreach (var item in AllItems) item.IsSelected = value;
        });
        OnSelectionChanged();
    }

    private void OnAllItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (RenameItemViewModel item in e.NewItems) item.PropertyChanged += OnItemPropertyChanged;
        if (e.OldItems != null)
            foreach (RenameItemViewModel item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;

        if (_isBatchUpdating) return;

        // 단건 삭제는 목록 스크롤 위치가 유지되도록 표시 목록에서만 빼고, 그 외에는 다시 필터링한다.
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (RenameItemViewModel item in e.OldItems) DisplayItems.Remove(item);
        }
        else
        {
            FilterList();
        }
        UpdateCounts();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isBatchUpdating && e.PropertyName == nameof(RenameItemViewModel.IsSelected))
            OnSelectionChanged();
    }

    // 선택 해제로 충돌이 풀릴 수 있으므로 선택이 바뀌면 충돌 표시도 다시 계산한다.
    private void OnSelectionChanged()
    {
        CheckConflicts();
        UpdateCounts();
    }

    private void RunBatch(Action action)
    {
        bool wasBatching = _isBatchUpdating;
        _isBatchUpdating = true;
        try { action(); }
        finally { _isBatchUpdating = wasBatching; }
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

        void Add(string path, bool isDirectory)
        {
            if (existingPaths.Add(path))
                AllItems.Add(new RenameItemViewModel(path, isDirectory));
        }

        RunBatch(() =>
        {
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

                    if (ShouldIncludeFile(path)) Add(path, false);
                }
                else if (Directory.Exists(path))
                {
                    if (IncludeFolders) Add(path, true);

                    try
                    {
                        var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                        if (IncludeFiles)
                        {
                            var pattern = OnlyWavFiles ? "*.wav" : "*";
                            foreach (var subFile in Directory.EnumerateFiles(path, pattern, searchOption))
                            {
                                if (ShouldIncludeFile(subFile)) Add(subFile, false);
                            }
                        }

                        if (IncludeFolders && IncludeSubfolders)
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                            {
                                Add(subDir, true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"폴더 탐색 중 오류: {ex.Message}";
                    }
                }
            }
        });

        RefreshPreview();
        FilterList();
    }

    private bool ShouldIncludeFile(string filePath)
    {
        if (!IncludeFiles) return false;
        return !OnlyWavFiles || Path.GetExtension(filePath).Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void SelectOnlyWavFiles()
    {
        RunBatch(() =>
        {
            foreach (var item in AllItems) item.IsSelected = item.IsWavFile;
        });
        OnSelectionChanged();
    }

    [RelayCommand]
    public void RemoveNonWavFiles()
    {
        var nonWav = AllItems.Where(x => !x.IsWavFile).ToList();
        RunBatch(() =>
        {
            foreach (var item in nonWav) AllItems.Remove(item);
        });
        RefreshPreview();
        FilterList();
        StatusMessage = $"{nonWav.Count}개의 비-WAV 항목을 목록에서 제거했습니다.";
    }

    [RelayCommand]
    public void RemoveItem(RenameItemViewModel item)
    {
        AllItems.Remove(item);
        RefreshPreview();
    }

    [RelayCommand]
    public void ClearAll()
    {
        AllItems.Clear();
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

    private RenameOptions CurrentOptions => new(
        SearchText, ReplaceText, UseRegex, MatchAll, MatchCase, SelectedScope, SelectedFormatting);

    public void RefreshPreview()
    {
        // 정규식은 키 입력 1회당 한 번만 컴파일한다 (이전: 항목 수만큼 생성).
        var engine = RenameEngine.Create(CurrentOptions);
        string? firstError = engine.Error;

        foreach (var item in AllItems)
        {
            item.NewName = engine.Transform(item.OriginalName, item.IsDirectory, out var error);
            firstError ??= error;
        }

        GlobalErrorMessage = firstError;

        CheckConflicts();
        UpdateCounts();
    }

    private void CheckConflicts()
    {
        var conflicted = AllItems
            .Where(x => x.IsSelected && x.IsModified)
            .GroupBy(x => x.TargetPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Skip(1).Any())
            .SelectMany(g => g)
            .ToHashSet();

        foreach (var item in AllItems)
        {
            bool hasConflict = conflicted.Contains(item);
            item.HasConflict = hasConflict;
            item.Status = hasConflict ? RenameStatus.Conflict : RenameStatus.Pending;
            item.StatusMessage = hasConflict ? "중복된 대상 파일명이 존재합니다." : null;
        }
    }

    public void FilterList()
    {
        var filter = FilterText.Trim();
        DisplayItems = string.IsNullOrEmpty(filter)
            ? new ObservableCollection<RenameItemViewModel>(AllItems)
            : new ObservableCollection<RenameItemViewModel>(AllItems.Where(item =>
                item.OriginalName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.NewName.Contains(filter, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(OriginalCount));
        OnPropertyChanged(nameof(RenamedCount));
        OnPropertyChanged(nameof(WavFileCount));
    }

    private sealed record MovePlan(RenameItemViewModel Item, string OldPath, string NewPath, bool IsDirectory);

    private sealed record MoveResult(MovePlan Plan, string? Error);

    [RelayCommand]
    public async Task ApplyRenameAsync()
    {
        // 하위 경로부터 처리해야 상위 폴더 이름이 먼저 바뀌어 경로가 깨지는 일을 막는다.
        var plans = AllItems
            .Where(x => x.IsSelected && x.IsModified && !x.HasConflict)
            .Select(x => new MovePlan(x, x.FullPath, x.TargetPath, x.IsDirectory))
            .OrderByDescending(p => p.OldPath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ToList();

        if (plans.Count == 0)
        {
            StatusMessage = "이름을 바꿀 선택된 항목이 없습니다.";
            return;
        }

        string? bmsPath = SyncWithBms && HasDetectedBms ? DetectedBmsPath : null;
        StatusMessage = $"{plans.Count}개 항목 이름 변경 중...";

        // 디스크 I/O는 UI 스레드 밖에서 수행한다 (수천 개 WAV에서 창이 멈추지 않도록).
        var (results, bmsBackup, bmsSyncCount, bmsError) = await Task.Run(() =>
        {
            byte[]? backup = null;
            if (bmsPath != null)
            {
                try { backup = File.ReadAllBytes(bmsPath); } catch { /* 실행 취소 시 BMS 복구만 생략 */ }
            }

            var moveResults = new List<MoveResult>(plans.Count);
            foreach (var plan in plans)
            {
                try
                {
                    FileRenamer.Move(plan.OldPath, plan.NewPath, plan.IsDirectory);
                    moveResults.Add(new MoveResult(plan, null));
                }
                catch (Exception ex)
                {
                    moveResults.Add(new MoveResult(plan, ex.Message));
                }
            }

            // BMS에는 디스크에서 실제로 성공한 변경만 반영한다.
            int synced = 0;
            string? syncError = null;
            var succeeded = moveResults.Where(r => r.Error == null).Select(r => ToRenamedPath(r.Plan)).ToList();
            if (bmsPath != null && succeeded.Count > 0)
            {
                synced = BmsSyncService.SyncBmsFile(bmsPath, succeeded, out syncError);
            }

            return (moveResults, backup, synced, syncError);
        });

        var history = new List<RenamedPath>();
        foreach (var (plan, error) in results)
        {
            if (error == null)
            {
                plan.Item.MarkMovedTo(plan.NewPath);
                if (plan.IsDirectory) RebaseChildren(plan.OldPath, plan.NewPath);
                history.Add(ToRenamedPath(plan));
            }
        }

        if (history.Count > 0)
        {
            _undoHistory.Push(new UndoEntry(history, bmsPath, bmsSyncCount > 0 ? bmsBackup : null));
            OnPropertyChanged(nameof(CanUndo));
        }

        RefreshPreview();

        // RefreshPreview가 상태를 초기화하므로 실패 표시는 그 뒤에 남긴다.
        var failures = results.Where(r => r.Error != null).ToList();
        foreach (var (plan, error) in failures)
        {
            plan.Item.Status = RenameStatus.Error;
            plan.Item.StatusMessage = error;
        }

        string bmsMsg = bmsPath != null ? $" (BMS 동기화: {bmsSyncCount}개 행 갱신)" : "";
        StatusMessage = $"완료: {history.Count}개 변경 성공{bmsMsg}"
            + (failures.Count > 0 ? $", {failures.Count}개 실패 (첫 오류: {failures[0].Error})" : "")
            + (bmsError != null ? $" [BMS 오류: {bmsError}]" : "");
    }

    private static RenamedPath ToRenamedPath(MovePlan plan) => new(plan.OldPath, plan.NewPath, plan.IsDirectory);

    /// <summary>폴더 이름이 바뀐 뒤, 그 아래에 있던 목록 항목들의 경로를 새 폴더 기준으로 고친다.</summary>
    private void RebaseChildren(string oldDir, string newDir)
    {
        string oldPrefix = Path.TrimEndingDirectorySeparator(oldDir) + Path.DirectorySeparatorChar;
        foreach (var item in AllItems)
        {
            if (item.FullPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                item.FullPath = Path.Combine(newDir, item.FullPath[oldPrefix.Length..]);
                item.DirectoryPath = Path.GetDirectoryName(item.FullPath) ?? item.DirectoryPath;
            }
        }
    }

    [RelayCommand]
    public async Task UndoRenameAsync()
    {
        if (_undoHistory.Count == 0) return;

        var entry = _undoHistory.Pop();
        OnPropertyChanged(nameof(CanUndo));

        // 적용 순서의 역순으로 되돌린다 (상위 폴더 → 하위 항목).
        var reversed = Enumerable.Reverse(entry.Files).ToList();
        var (restored, failCount) = await Task.Run(() =>
        {
            var ok = new List<RenamedPath>();
            int failed = 0;
            foreach (var rename in reversed)
            {
                bool exists = rename.IsDirectory ? Directory.Exists(rename.NewPath) : File.Exists(rename.NewPath);
                if (!exists) { failed++; continue; }
                try
                {
                    FileRenamer.Move(rename.NewPath, rename.OldPath, rename.IsDirectory);
                    ok.Add(rename);
                }
                catch
                {
                    failed++;
                }
            }

            // BMS는 동기화 직전의 원본 바이트로 복구 (인코딩·BOM 보존)
            if (entry.BmsPath != null && entry.BmsBackup != null && File.Exists(entry.BmsPath))
            {
                try { File.WriteAllBytes(entry.BmsPath, entry.BmsBackup); } catch { /* 상태 메시지로 충분 */ }
            }

            return (ok, failed);
        });

        // 폴더 복구는 하위 항목 경로를 연쇄적으로 바꾸므로 그때는 매번 직접 찾고,
        // 파일만 있는 일반적인 경우에는 사전 조회로 O(n) 에 처리한다.
        bool hasDirectories = restored.Any(r => r.IsDirectory);
        var byPath = hasDirectories ? null : AllItems
            .GroupBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        RenameItemViewModel? Find(string path) => byPath != null
            ? byPath.GetValueOrDefault(path)
            : AllItems.FirstOrDefault(x => x.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));

        foreach (var rename in restored)
        {
            if (Find(rename.NewPath) is { } item)
            {
                item.MarkMovedTo(rename.OldPath);
                if (rename.IsDirectory) RebaseChildren(rename.NewPath, rename.OldPath);
            }
        }

        string bmsMsg = entry.BmsBackup != null ? " 및 BMS 파일" : "";
        StatusMessage = $"실행 취소 완료: {restored.Count}개 파일{bmsMsg} 복구됨" + (failCount > 0 ? $", {failCount}개 실패" : "");
        RefreshPreview();
    }
}
