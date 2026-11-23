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
    [NotifyPropertyChangedFor(nameof(SelectedScopeIndex))]
    private RenameScope _selectedScope = RenameScope.NameAndExtension;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsLowercaseFormatting),
        nameof(IsUppercaseFormatting),
        nameof(IsTitleCaseFormatting),
        nameof(IsSentenceCaseFormatting))]
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

    private bool _isAllSelected = true;
    private bool _suppressAllSelectedChanged;

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
    private readonly HashSet<string> _sourceDirectories = new(StringComparer.OrdinalIgnoreCase);

    // 대량 추가/선택 변경 중에는 항목별 재계산을 막고 끝에서 한 번만 수행한다.
    private bool _isBatchUpdating;
    private int _originalCount;
    private int _renamedCount;
    private int _wavFileCount;

    private sealed record UndoEntry(List<RenamedPath> Files, string? BmsPath, byte[]? BmsBackup);

    public int OriginalCount => _originalCount;
    public int RenamedCount => _renamedCount;
    public int WavFileCount => _wavFileCount;
    public bool CanUndo => _undoHistory.Count > 0;

    public bool IsAllSelected
    {
        get => _isAllSelected;
        set
        {
            if (SetProperty(ref _isAllSelected, value) && !_suppressAllSelectedChanged)
                SetAllSelected(value);
        }
    }

    public int SelectedScopeIndex
    {
        get => (int)SelectedScope;
        set
        {
            if (Enum.IsDefined(typeof(RenameScope), value))
                SelectedScope = (RenameScope)value;
        }
    }

    public bool IsLowercaseFormatting => SelectedFormatting == TextFormatting.Lowercase;
    public bool IsUppercaseFormatting => SelectedFormatting == TextFormatting.Uppercase;
    public bool IsTitleCaseFormatting => SelectedFormatting == TextFormatting.TitleCase;
    public bool IsSentenceCaseFormatting => SelectedFormatting == TextFormatting.SentenceCase;

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

    partial void OnIncludeFilesChanged(bool value) => SyncItemsWithInclusionOptions();
    partial void OnIncludeFoldersChanged(bool value) => SyncItemsWithInclusionOptions();
    partial void OnIncludeSubfoldersChanged(bool value) => SyncItemsWithInclusionOptions();
    partial void OnOnlyWavFilesChanged(bool value) => SyncItemsWithInclusionOptions();

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
        SyncIsAllSelected();
    }

    private void SyncIsAllSelected()
    {
        bool allSelected = AllItems.Count > 0 && AllItems.All(x => x.IsSelected);
        if (_isAllSelected == allSelected) return;

        _suppressAllSelectedChanged = true;
        try { IsAllSelected = allSelected; }
        finally { _suppressAllSelectedChanged = false; }
    }

    private void SetAllSelected(bool value)
    {
        RunBatch(() =>
        {
            foreach (var item in AllItems) item.IsSelected = value;
        });
        OnSelectionChanged();
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

                    if (ShouldIncludeFile(path) && existingPaths.Add(path))
                        AllItems.Add(new RenameItemViewModel(path, false));
                }
                else if (Directory.Exists(path))
                {
                    string cleanDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                    _sourceDirectories.Add(cleanDir);
                    AddDirectoryContents(cleanDir, existingPaths);
                }
            }
        });

        RefreshPreview();
        FilterList();
        SyncIsAllSelected();
    }

    private void AddDirectoryContents(string dirPath, HashSet<string> existingPaths)
    {
        void Add(string itemPath, bool isDirectory)
        {
            if (existingPaths.Add(itemPath))
                AllItems.Add(new RenameItemViewModel(itemPath, isDirectory));
        }

        if (IncludeFolders) Add(dirPath, true);

        try
        {
            var scanOptions = FileScanner.Options(IncludeSubfolders);

            if (IncludeFiles)
            {
                var pattern = OnlyWavFiles ? "*.wav" : "*";
                foreach (var subFile in Directory.EnumerateFiles(dirPath, pattern, scanOptions))
                {
                    if (ShouldIncludeFile(subFile)) Add(subFile, false);
                }
            }

            if (IncludeFolders && IncludeSubfolders)
            {
                foreach (var subDir in Directory.EnumerateDirectories(dirPath, "*", FileScanner.Options(true)))
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

    private void SyncItemsWithInclusionOptions()
    {
        if (AllItems.Count == 0 && _sourceDirectories.Count == 0)
            return;

        if (_sourceDirectories.Count > 0)
        {
            var existingPaths = new HashSet<string>(AllItems.Select(x => x.FullPath), StringComparer.OrdinalIgnoreCase);
            RunBatch(() =>
            {
                foreach (var dir in _sourceDirectories)
                {
                    if (Directory.Exists(dir))
                        AddDirectoryContents(dir, existingPaths);
                }
            });
        }

        RunBatch(() =>
        {
            foreach (var item in AllItems)
                item.IsSelected = ShouldSelectItemByOptions(item);
        });

        RefreshPreview();
        FilterList();
        SyncIsAllSelected();
    }

    private bool ShouldSelectItemByOptions(RenameItemViewModel item)
    {
        if (item.IsDirectory)
        {
            if (!IncludeFolders) return false;
            return IncludeSubfolders || !IsStrictSubdirectoryOfSource(item.FullPath);
        }

        if (!IncludeFiles) return false;
        if (OnlyWavFiles && !item.IsWavFile) return false;
        return IncludeSubfolders || !IsStrictSubdirectoryOfSource(item.DirectoryPath);
    }

    private bool IsStrictSubdirectoryOfSource(string dirPath)
    {
        if (_sourceDirectories.Count == 0) return false;
        string clean = Path.TrimEndingDirectorySeparator(dirPath);
        if (_sourceDirectories.Contains(clean)) return false;

        foreach (var srcDir in _sourceDirectories)
        {
            if (clean.StartsWith(srcDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
        var wavItems = new List<RenameItemViewModel>(AllItems.Count);
        int removedCount = 0;

        foreach (var item in AllItems)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            if (item.IsWavFile)
                wavItems.Add(item);
            else
                removedCount++;
        }

        if (removedCount == 0)
        {
            StatusMessage = "제거할 비-WAV 항목이 없습니다.";
            return;
        }

        RunBatch(() =>
        {
            AllItems.Clear();
            foreach (var item in wavItems) AllItems.Add(item);
        });

        RefreshPreview();
        FilterList();
        SyncIsAllSelected();
        StatusMessage = $"{removedCount}개의 비-WAV 항목을 목록에서 제거했습니다.";
    }

    [RelayCommand]
    public void RemoveItem(RenameItemViewModel item)
    {
        AllItems.Remove(item);
        RefreshPreview();
        SyncIsAllSelected();
    }

    [RelayCommand]
    public void ClearAll()
    {
        foreach (var item in AllItems)
            item.PropertyChanged -= OnItemPropertyChanged;

        RunBatch(() => AllItems.Clear());
        _sourceDirectories.Clear();
        DisplayItems = [];
        GlobalErrorMessage = null;
        OnSelectionChanged();
        StatusMessage = "목록을 비웠습니다.";
    }

    [RelayCommand]
    public void ClearBms()
    {
        DetectedBmsPath = null;
    }

    [RelayCommand]
    public void SelectAll() => SetAllSelected(true);

    [RelayCommand]
    public void DeselectAll() => SetAllSelected(false);

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

        // 필터는 새 이름도 검사하므로, 필터가 켜진 상태에서 치환 규칙이 바뀌면 표시 목록도 다시 걸러야 한다.
        if (!string.IsNullOrWhiteSpace(FilterText) && !_isBatchUpdating)
            FilterList();
    }

    private sealed class DirectoryAndNameComparer : IEqualityComparer<(string Dir, string Name)>
    {
        public static readonly DirectoryAndNameComparer Instance = new();

        public bool Equals((string Dir, string Name) x, (string Dir, string Name) y)
            => string.Equals(x.Dir, y.Dir, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Dir, string Name) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Dir),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }

    private void CheckConflicts()
    {
        var stationarySet = new HashSet<(string Dir, string Name)>(DirectoryAndNameComparer.Instance);
        var targetCounts = new Dictionary<(string Dir, string Name), int>(DirectoryAndNameComparer.Instance);

        foreach (var item in AllItems)
        {
            if (item.IsSelected && item.IsModified)
            {
                var key = (item.DirectoryPath, item.NewName);
                targetCounts[key] = targetCounts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
            else
            {
                stationarySet.Add((item.DirectoryPath, item.OriginalName));
            }
        }

        foreach (var item in AllItems)
        {
            bool hasConflict = false;
            if (item.IsSelected && item.IsModified)
            {
                var key = (item.DirectoryPath, item.NewName);
                hasConflict = targetCounts.GetValueOrDefault(key) > 1 || stationarySet.Contains(key);
            }

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
        int original = AllItems.Count;
        int renamed = 0;
        int wav = 0;
        foreach (var item in AllItems)
        {
            if (item.IsSelected && item.IsModified) renamed++;
            if (item.IsWavFile) wav++;
        }

        if (_originalCount != original)
        {
            _originalCount = original;
            OnPropertyChanged(nameof(OriginalCount));
        }
        if (_renamedCount != renamed)
        {
            _renamedCount = renamed;
            OnPropertyChanged(nameof(RenamedCount));
        }
        if (_wavFileCount != wav)
        {
            _wavFileCount = wav;
            OnPropertyChanged(nameof(WavFileCount));
        }
    }

    private sealed record MovePlan(RenameItemViewModel Item, string OldPath, string NewPath, bool IsDirectory);

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

            var batchResults = FileRenamer.MoveBatch(plans.Select(ToRenamedPath).ToList());

            // BMS에는 디스크에서 실제로 성공한 변경만 반영한다.
            int synced = 0;
            string? syncError = null;
            var succeeded = batchResults.Where(r => r.Error == null).Select(r => r.Plan).ToList();
            if (bmsPath != null && succeeded.Count > 0)
            {
                synced = BmsSyncService.SyncBmsFile(bmsPath, succeeded, out syncError);
            }

            return (batchResults, backup, synced, syncError);
        });

        var history = new List<RenamedPath>();
        var failures = new List<(MovePlan Plan, string Error)>();
        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var error = results[i].Error;
            if (error == null)
            {
                plan.Item.MarkMovedTo(plan.NewPath);
                if (plan.IsDirectory) RebaseChildren(plan.OldPath, plan.NewPath);
                history.Add(results[i].Plan);
            }
            else
            {
                failures.Add((plan, error));
            }
        }

        if (history.Count > 0)
        {
            _undoHistory.Push(new UndoEntry(history, bmsPath, bmsSyncCount > 0 ? bmsBackup : null));
            OnPropertyChanged(nameof(CanUndo));
        }

        RefreshPreview();

        // RefreshPreview가 상태를 초기화하므로 실패 표시는 그 뒤에 남긴다.
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
        string oldClean = Path.TrimEndingDirectorySeparator(oldDir);
        string newClean = Path.TrimEndingDirectorySeparator(newDir);
        string oldPrefix = oldClean + Path.DirectorySeparatorChar;

        foreach (var item in AllItems)
        {
            if (item.FullPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                item.FullPath = Path.Combine(newClean, item.FullPath[oldPrefix.Length..]);
                item.DirectoryPath = Path.GetDirectoryName(item.FullPath) ?? item.DirectoryPath;
            }
        }

        if (_sourceDirectories.Remove(oldClean))
            _sourceDirectories.Add(newClean);

        var nestedSources = _sourceDirectories
            .Where(s => s.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var s in nestedSources)
        {
            _sourceDirectories.Remove(s);
            _sourceDirectories.Add(Path.Combine(newClean, s[oldPrefix.Length..]));
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
            var undoPlans = reversed
                .Select(r => new RenamedPath(r.NewPath, r.OldPath, r.IsDirectory))
                .ToList();

            var batchResults = FileRenamer.MoveBatch(undoPlans);
            var ok = new List<RenamedPath>(batchResults.Count);
            int failed = 0;
            for (int i = 0; i < batchResults.Count; i++)
            {
                if (batchResults[i].Error == null)
                    ok.Add(reversed[i]);
                else
                    failed++;
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
