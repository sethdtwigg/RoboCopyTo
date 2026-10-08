using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoboCopyTo.App.Controls;
using RoboCopyTo.App.Services;
using RoboCopyTo.Core;
using RoboCopyTo.Core.Execution;
using RoboCopyTo.Core.Logging;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;
using RoboCopyTo.Core.Presets;
using RoboCopyTo.Core.Safety;
using RoboCopyTo.Core.Shell;

namespace RoboCopyTo.App.ViewModels;

public enum ScreenMode { Edit, Running, Results }

public sealed partial class MainViewModel : ObservableObject
{
    public const int MaxOutputLines = 5000;

    private readonly IUserPrompts _prompts;
    private readonly PresetStore _presets;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly LogManager _logs;
    private readonly ElevationJob? _elevatedJob;
    private readonly string _previewLogPath;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _outputFlush;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly ConcurrentQueue<string> _pendingOutput = new();
    private bool _suppressRefresh;
    private CancellationTokenSource? _runCts;
    private Stopwatch? _runClock;
    private string? _lastLogPath;
    private bool _lastRunDryRun;
    private CancellationTokenSource? _fatCheck;

    public MainViewModel(IReadOnlyList<string> paths, IUserPrompts prompts, ElevationJob? elevatedJob = null,
        PresetStore? presets = null, string? settingsPath = null, LogManager? logs = null)
    {
        _prompts = prompts;
        _presets = presets ?? PresetStore.CreateDefault();
        _settingsPath = settingsPath ?? AppSettings.DefaultPath;
        _settings = AppSettings.Load(_settingsPath);
        _logs = logs ?? LogManager.CreateDefault();
        _elevatedJob = elevatedJob;
        _previewLogPath = _logs.NewLogPath(DateTime.Now, dryRun: false);
        // The view model is created before the dispatcher's synchronization context exists, so capture it explicitly.
        _dispatcher = Dispatcher.CurrentDispatcher;

        _outputFlush = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _outputFlush.Tick += (_, _) => FlushOutput();
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _elapsedTimer.Tick += (_, _) => ElapsedText = _runClock is null ? "" : Format.Elapsed(_runClock.Elapsed);

        foreach (var p in _presets.All)
            Presets.Add(p);
        foreach (var d in _settings.RecentDestinations)
            RecentDestinations.Add(d);

        var sources = elevatedJob?.Sources ?? paths;
        foreach (var item in SelectionAnalyzer.Analyze(sources, DiskPathProbe.Instance).Items)
            Items.Add(new SelectedItemViewModel(item));
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ItemsHeader));
            OnPropertyChanged(nameof(HasLooseFiles));
            OnPropertyChanged(nameof(IsMirrorEnabled));
            OnPropertyChanged(nameof(MirrorToolTip));
            Refresh();
        };

        _suppressRefresh = true;
        if (elevatedJob is not null)
        {
            Destination = elevatedJob.Destination;
            _selectedPreset = _presets.Find(elevatedJob.PresetName) ?? _presets.Find(Preset.QuickCopy)!;
            ApplyOptions(elevatedJob.Options);
        }
        else
        {
            Destination = _settings.RecentDestinations.FirstOrDefault() ?? "";
            _selectedPreset = _presets.Find(_settings.InitialPresetName(_presets))!;
            ApplyOptions(_selectedPreset.Options);
        }
        _suppressRefresh = false;
        Refresh();
    }

    public bool IsElevated => Elevation.IsElevated;

    public string WindowTitle => IsElevated ? "RoboCopyTo — Running as administrator" : "RoboCopyTo";

    // ───────────── Selected items ─────────────

    public ObservableCollection<SelectedItemViewModel> Items { get; } = [];

    public string ItemsHeader => $"Selected items ({Items.Count})";

    public bool HasLooseFiles => Items.Any(i => i.Item.Kind == ItemKind.File);

    [RelayCommand]
    private void RemoveItem(SelectedItemViewModel item) => Items.Remove(item);

    // ───────────── Destination ─────────────

    [ObservableProperty]
    public partial string Destination { get; set; } = "";

    public ObservableCollection<string> RecentDestinations { get; } = [];

    [RelayCommand]
    private void UseRecentDestination(string destination) => Destination = destination;

    [ObservableProperty]
    public partial bool IsDestinationFat { get; set; }

    public string FftNote => OptionHelp.Fft;

    partial void OnDestinationChanged(string value)
    {
        Refresh();
        CheckDestinationVolume(value);
    }

    partial void OnIsDestinationFatChanged(bool value) => Refresh();

    /// <summary>For the live preview: DriveInfo can be slow on network drives, so check off the UI thread.</summary>
    private void CheckDestinationVolume(string dest)
    {
        _fatCheck?.Cancel();
        var cts = _fatCheck = new CancellationTokenSource();
        var normalized = PreflightChecker.ValidateDestination(dest, DiskPathProbe.Instance, out _);
        if (normalized is null)
        {
            IsDestinationFat = false;
            return;
        }
        Task.Run(() => VolumeInfo.IsFatDestination(normalized), cts.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                _dispatcher.BeginInvoke(() =>
                {
                    if (!cts.IsCancellationRequested)
                        IsDestinationFat = t.Result;
                });
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private void Browse()
    {
        var initial = Directory.Exists(Destination) ? Destination : null;
        var picked = _prompts.BrowseFolder(initial);
        if (picked is not null)
            Destination = picked;
    }

    // ───────────── Presets ─────────────

    public ObservableCollection<Preset> Presets { get; } = [];

    private Preset _selectedPreset;

    public Preset SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedPreset))
                return;
            if (IsModified && !_prompts.Confirm("Switch preset",
                    $"Your changes to \"{_selectedPreset.Name}\" are not saved. Switch to \"{value.Name}\" and discard them?"))
            {
                // Put the dropdown back once the current selection change has finished.
                _dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedPreset)), DispatcherPriority.ContextIdle);
                return;
            }
            _selectedPreset = value;
            OnPropertyChanged();
            ApplyOptions(value.Options);
        }
    }

    public bool IsModified => CurrentOptions() != SelectedPreset.Options;

    public string PresetStateText => IsModified ? "(modified)" : "";

    public bool IsDefaultPreset
    {
        get => string.Equals(_settings.DefaultPreset, SelectedPreset.Name, StringComparison.OrdinalIgnoreCase);
        set
        {
            _settings.DefaultPreset = value ? SelectedPreset.Name : null;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        if (TrySavePreset(SelectedPreset with { Options = CurrentOptions() }) is { } saved)
            ReloadPresets(saved.Name);
    }

    private bool CanSavePreset() => !SelectedPreset.IsBuiltIn && IsModified;

    [RelayCommand]
    private void SavePresetAs()
    {
        var suggestion = SelectedPreset.IsBuiltIn ? SelectedPreset.Name + " (copy)" : SelectedPreset.Name;
        var name = _prompts.AskText("Save preset as", "Preset name:", suggestion)?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        if (PresetStore.IsBuiltInName(name))
        {
            _prompts.ShowError("Save preset as", $"\"{name}\" is a built-in preset. Choose another name.");
            return;
        }
        if (_presets.Find(name) is not null && !_prompts.Confirm("Save preset as", $"A preset named \"{name}\" already exists. Replace it?"))
            return;
        if (TrySavePreset(new Preset(name, CurrentOptions())) is { } saved)
            ReloadPresets(saved.Name);
    }

    private Preset? TrySavePreset(Preset preset)
    {
        try
        {
            return _presets.Save(preset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _prompts.ShowError("Save preset", $"The preset could not be saved to {_presets.FilePath}:\n\n{ex.Message}");
            return null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        var name = SelectedPreset.Name;
        if (!_prompts.Confirm("Delete preset", $"Delete the preset \"{name}\"?"))
            return;
        try
        {
            _presets.Delete(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _prompts.ShowError("Delete preset", $"The preset could not be deleted from {_presets.FilePath}:\n\n{ex.Message}");
            return;
        }
        if (string.Equals(_settings.DefaultPreset, name, StringComparison.OrdinalIgnoreCase))
            _settings.DefaultPreset = null;
        if (string.Equals(_settings.LastUsedPreset, name, StringComparison.OrdinalIgnoreCase))
            _settings.LastUsedPreset = null;
        SaveSettings();
        // Keep the current options; show them relative to Quick copy.
        ReloadPresets(Preset.QuickCopy);
    }

    private bool CanDeletePreset() => !SelectedPreset.IsBuiltIn;

    [RelayCommand(CanExecute = nameof(IsModified))]
    private void RevertPreset() => ApplyOptions(SelectedPreset.Options);

    private void ReloadPresets(string select)
    {
        Presets.Clear();
        foreach (var p in _presets.All)
            Presets.Add(p);
        _selectedPreset = _presets.Find(select) ?? _presets.Find(Preset.QuickCopy)!;
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(IsDefaultPreset));
        Refresh();
    }

    // ───────────── Options ─────────────

    [ObservableProperty] public partial bool IncludeSubfolders { get; set; }
    [ObservableProperty] public partial bool Mirror { get; set; }
    [ObservableProperty] public partial bool Restartable { get; set; }
    [ObservableProperty] public partial bool Unbuffered { get; set; }
    [ObservableProperty] public partial bool Multithreaded { get; set; }
    [ObservableProperty] public partial int Threads { get; set; }
    [ObservableProperty] public partial ExistingFileMode ExistingFiles { get; set; }
    [ObservableProperty] public partial int RetryCount { get; set; }
    [ObservableProperty] public partial int RetryWaitSeconds { get; set; }
    [ObservableProperty] public partial bool NetworkFriendly { get; set; }
    [ObservableProperty] public partial bool CopyPermissions { get; set; }
    [ObservableProperty] public partial bool CopyOwner { get; set; }
    [ObservableProperty] public partial bool CopyAuditing { get; set; }
    [ObservableProperty] public partial bool BackupMode { get; set; }
    [ObservableProperty] public partial bool RestartableBackup { get; set; }
    [ObservableProperty] public partial bool SkipJunctions { get; set; }
    [ObservableProperty] public partial bool KeepFolderTimestamps { get; set; }
    [ObservableProperty] public partial string ExcludeFiles { get; set; } = "";
    [ObservableProperty] public partial string ExcludeFolders { get; set; } = "";
    [ObservableProperty] public partial string ExtraArguments { get; set; } = "";

    [ObservableProperty] public partial bool IsAdvancedExpanded { get; set; }

    public IReadOnlyList<ExistingModeChoice> ExistingModes { get; } =
        Enum.GetValues<ExistingFileMode>().Select(m => new ExistingModeChoice(m, m.DisplayName(), OptionHelp.ForExistingMode(m))).ToList();

    /// <summary>/E shows ticked and locked while Mirror is on.</summary>
    public bool DisplayIncludeSubfolders
    {
        get => Mirror || IncludeSubfolders;
        set
        {
            if (!Mirror)
                IncludeSubfolders = value;
        }
    }

    public bool IsIncludeSubfoldersEnabled => !Mirror;
    public string IncludeSubfoldersToolTip => Mirror ? OptionHelp.IncludeSubfoldersLocked : OptionHelp.IncludeSubfolders;

    /// <summary>Disabled while loose files are selected, unless it is already on (so it can be turned off).</summary>
    public bool IsMirrorEnabled => !HasLooseFiles || Mirror;
    public string MirrorToolTip => HasLooseFiles ? OptionHelp.MirrorDisabled : OptionHelp.Mirror;

    public bool IsRestartableEnabled => !RestartableBackup;
    public string RestartableToolTip => RestartableBackup ? OptionHelp.RestartableReplaced : OptionHelp.Restartable;

    /// <summary>Spinners show the Network-friendly values while it is on; the user's values are kept.</summary>
    public int DisplayRetryCount
    {
        get => NetworkFriendly ? RetryProfile.NetworkFriendly.Count : RetryCount;
        set
        {
            if (!NetworkFriendly)
                RetryCount = value;
        }
    }

    public int DisplayRetryWait
    {
        get => NetworkFriendly ? RetryProfile.NetworkFriendly.WaitSeconds : RetryWaitSeconds;
        set
        {
            if (!NetworkFriendly)
                RetryWaitSeconds = value;
        }
    }

    public bool AreRetrySpinnersEnabled => !NetworkFriendly;

    partial void OnRestartableChanged(bool value)
    {
        if (value && RestartableBackup && !_suppressRefresh)
            RestartableBackup = false;
    }

    partial void OnRestartableBackupChanged(bool value)
    {
        if (value && Restartable && !_suppressRefresh)
            Restartable = false;
    }

    private static readonly HashSet<string> OptionProperties =
    [
        nameof(IncludeSubfolders), nameof(Mirror), nameof(Restartable), nameof(Unbuffered), nameof(Multithreaded),
        nameof(Threads), nameof(ExistingFiles), nameof(RetryCount), nameof(RetryWaitSeconds), nameof(NetworkFriendly),
        nameof(CopyPermissions), nameof(CopyOwner), nameof(CopyAuditing), nameof(BackupMode), nameof(RestartableBackup),
        nameof(SkipJunctions), nameof(KeepFolderTimestamps), nameof(ExcludeFiles), nameof(ExcludeFolders), nameof(ExtraArguments),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null && OptionProperties.Contains(e.PropertyName))
            Refresh();
    }

    public RobocopyOptions CurrentOptions() => new RobocopyOptions
    {
        IncludeSubfolders = IncludeSubfolders,
        Mirror = Mirror,
        Restartable = Restartable,
        Unbuffered = Unbuffered,
        Multithreaded = Multithreaded,
        Threads = Threads,
        ExistingFiles = ExistingFiles,
        RetryCount = RetryCount,
        RetryWaitSeconds = RetryWaitSeconds,
        NetworkFriendly = NetworkFriendly,
        CopyPermissions = CopyPermissions,
        CopyOwner = CopyOwner,
        CopyAuditing = CopyAuditing,
        BackupMode = BackupMode,
        RestartableBackup = RestartableBackup,
        SkipJunctions = SkipJunctions,
        KeepFolderTimestamps = KeepFolderTimestamps,
        ExcludeFiles = ExcludeFiles,
        ExcludeFolders = ExcludeFolders,
        ExtraArguments = ExtraArguments,
    }.Sanitized();

    private void ApplyOptions(RobocopyOptions o)
    {
        var outer = _suppressRefresh;
        _suppressRefresh = true;
        IncludeSubfolders = o.IncludeSubfolders;
        Mirror = o.Mirror;
        Restartable = o.Restartable;
        Unbuffered = o.Unbuffered;
        Multithreaded = o.Multithreaded;
        Threads = o.Threads;
        ExistingFiles = o.ExistingFiles;
        RetryCount = o.RetryCount;
        RetryWaitSeconds = o.RetryWaitSeconds;
        NetworkFriendly = o.NetworkFriendly;
        CopyPermissions = o.CopyPermissions;
        CopyOwner = o.CopyOwner;
        CopyAuditing = o.CopyAuditing;
        BackupMode = o.BackupMode;
        RestartableBackup = o.RestartableBackup;
        SkipJunctions = o.SkipJunctions;
        KeepFolderTimestamps = o.KeepFolderTimestamps;
        ExcludeFiles = o.ExcludeFiles;
        ExcludeFolders = o.ExcludeFolders;
        ExtraArguments = o.ExtraArguments;
        _suppressRefresh = outer;
        if (!outer)
            Refresh();
    }

    // ───────────── Preview and derived state ─────────────

    public ObservableCollection<PreviewJobViewModel> Preview { get; } = [];

    [ObservableProperty] public partial string PreviewMessage { get; set; } = "";

    public bool RequiresElevation => CurrentOptions().RequiresElevation;

    public bool ShowStartShield => RequiresElevation && !IsElevated;

    private static readonly string[] DerivedProperties =
    [
        nameof(DisplayIncludeSubfolders), nameof(IsIncludeSubfoldersEnabled), nameof(IncludeSubfoldersToolTip),
        nameof(IsMirrorEnabled), nameof(MirrorToolTip), nameof(IsRestartableEnabled), nameof(RestartableToolTip),
        nameof(DisplayRetryCount), nameof(DisplayRetryWait), nameof(AreRetrySpinnersEnabled),
        nameof(IsModified), nameof(PresetStateText), nameof(RequiresElevation), nameof(ShowStartShield),
        nameof(IsDefaultPreset),
    ];

    /// <summary>Recomputes the preview and everything derived from the options. Runs on every change.</summary>
    private void Refresh()
    {
        if (_suppressRefresh)
            return;

        foreach (var name in DerivedProperties)
            base.OnPropertyChanged(new PropertyChangedEventArgs(name));
        SavePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
        RevertPresetCommand.NotifyCanExecuteChanged();

        Preview.Clear();
        var jobs = BuildJobs(_previewLogPath, dryRun: false, out var error);
        if (jobs is null)
        {
            PreviewMessage = error!;
            return;
        }
        foreach (var j in jobs)
            Preview.Add(new PreviewJobViewModel(j));
        PreviewMessage = jobs.Count == 0 ? "Nothing to copy." : "";
    }

    /// <summary>Jobs for the current dialog state, or null with the reason if the destination is unusable.</summary>
    private IReadOnlyList<RobocopyJob>? BuildJobs(string logPath, bool dryRun, out string? error)
    {
        var dest = PreflightChecker.ValidateDestination(Destination, DiskPathProbe.Instance, out error);
        if (dest is null)
        {
            error ??= "Choose a destination folder.";
            return null;
        }
        return CommandBuilder.Build(Items.Select(i => i.Item).ToList(), dest, CurrentOptions(), new BuildContext(logPath, dryRun, IsDestinationFat));
    }

    [RelayCommand]
    private void CopyPreview()
    {
        var text = string.Join(Environment.NewLine, Preview.Select(p => p.Job.CommandLine));
        if (text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException ex)
        {
            _prompts.ShowError("Copy commands", "The clipboard is in use by another program. Try again in a moment.\n\n" + ex.Message);
        }
    }

    // ───────────── Screens ─────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode), nameof(IsRunningMode), nameof(IsResultsMode))]
    public partial ScreenMode Mode { get; set; } = ScreenMode.Edit;

    public bool IsEditMode => Mode == ScreenMode.Edit;
    public bool IsRunningMode => Mode == ScreenMode.Running;
    public bool IsResultsMode => Mode == ScreenMode.Results;

    // ───────────── Start / Dry run ─────────────

    [RelayCommand]
    private Task Start() => BeginAsync(dryRun: false, skipConfirmations: false);

    [RelayCommand]
    private Task DryRun() => BeginAsync(dryRun: true, skipConfirmations: false);

    [RelayCommand]
    private void Close() => _prompts.CloseWindow();

    /// <summary>Called once the window is shown.</summary>
    public async Task OnLoadedAsync()
    {
        foreach (var warning in new[] { _presets.LoadWarning, _settings.LoadWarning })
            if (warning is not null)
                _prompts.ShowInfo("RoboCopyTo settings", warning);

        // The elevated process starts the run the user already confirmed.
        if (_elevatedJob is not null)
            await BeginAsync(_elevatedJob.DryRun, skipConfirmations: true);
    }

    private async Task BeginAsync(bool dryRun, bool skipConfirmations)
    {
        var title = dryRun ? "Dry run" : "Start";
        var items = Items.Select(i => i.Item).ToList();
        var options = CurrentOptions();

        var dest = PreflightChecker.ValidateDestination(Destination, DiskPathProbe.Instance, out var destError);
        if (dest is not null)
        {
            // Decide /FFT now rather than trusting the background check, which may not have finished.
            IsDestinationFat = VolumeInfo.IsFatDestination(dest);
        }

        // Build first, so nothing is saved or created for a run that cannot happen.
        var plannedJobs = BuildJobs(_previewLogPath, dryRun, out _);
        var check = PreflightChecker.Check(new PreflightInput(items, Destination, options, DiskPathProbe.Instance,
            Jobs: plannedJobs, Canonicalize: MappedDrives.ToUnc));
        if (check.IsBlocked)
        {
            _prompts.ShowError(title, string.Join(Environment.NewLine + Environment.NewLine, check.Blockers.Select(FormatIssue)));
            return;
        }
        if (dest is null || plannedJobs is null || plannedJobs.Count == 0)
        {
            _prompts.ShowError(title, destError ?? "Nothing to copy.");
            return;
        }

        if (!skipConfirmations)
        {
            foreach (var issue in check.Confirmations)
            {
                if (!_prompts.Confirm(title, FormatIssue(issue) + Environment.NewLine + Environment.NewLine + "Continue?", warning: true))
                    return;
            }
            if (check.OfferCreateDestination && !dryRun && !_prompts.Confirm("Create destination", $"The destination folder {dest} does not exist. Create it?"))
                return;
        }

        _settings.AddRecentDestination(dest);
        _settings.LastUsedPreset = SelectedPreset.Name;
        SaveSettings();
        RefreshRecent();

        if (options.RequiresElevation && !IsElevated)
        {
            RelaunchElevated(items, dest, options, dryRun);
            return;
        }

        if (check.OfferCreateDestination && !dryRun)
        {
            try
            {
                Directory.CreateDirectory(dest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _prompts.ShowError("Create destination", $"Could not create {dest}: {ex.Message}");
                return;
            }
            IsDestinationFat = VolumeInfo.IsFatDestination(dest);
        }

        await RunAsync(dryRun);
    }

    private static string FormatIssue(PreflightIssue issue)
        => issue.Message + (issue.Details.Count > 0
            ? Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, issue.Details.Select(d => "  • " + d))
            : "");

    private void RelaunchElevated(IReadOnlyList<SelectedItem> items, string dest, RobocopyOptions options, bool dryRun)
    {
        const string title = "Run as administrator";
        var job = new ElevationJob(items.Select(i => i.Path).ToList(), dest, options, dryRun, SelectedPreset.Name).WithUncPaths();
        string path, hash;
        try
        {
            (path, hash) = job.Write(ElevationJob.DefaultDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _prompts.ShowError(title, $"Could not write the job file to {ElevationJob.DefaultDirectory}:\n\n{ex.Message}");
            return;
        }

        var outcome = Elevation.TryRelaunch(path, hash, out var error);
        if (outcome == RelaunchOutcome.Started)
        {
            _prompts.CloseWindow();
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        if (outcome == RelaunchOutcome.Declined)
            _prompts.ShowInfo("Administrator permission declined",
                "Windows did not grant administrator rights, so nothing was copied.\n\nThe options marked with a shield need administrator rights. Untick them to copy without elevation, or click Start again.");
        else
            _prompts.ShowError(title, "RoboCopyTo could not restart as administrator, so nothing was copied.\n\n" + error);
    }

    private void RefreshRecent()
    {
        var keep = Destination;
        RecentDestinations.Clear();
        foreach (var d in _settings.RecentDestinations)
            RecentDestinations.Add(d);
        Destination = keep;
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; a failed save must not stop a copy.
        }
    }

    // ───────────── Running ─────────────

    public ObservableCollection<RunJobViewModel> RunJobs { get; } = [];
    public BulkObservableCollection<string> OutputLines { get; } = [];
    public ObservableCollection<FailedItemViewModel> FailedItems { get; } = [];

    [ObservableProperty] public partial bool IsDryRunActive { get; set; }
    [ObservableProperty] public partial bool IsScanning { get; set; }
    [ObservableProperty] public partial string ScanText { get; set; } = "";
    [ObservableProperty] public partial double ProgressPercent { get; set; }
    [ObservableProperty] public partial bool IsProgressIndeterminate { get; set; }
    [ObservableProperty] public partial string FilesText { get; set; } = "";
    [ObservableProperty] public partial string BytesText { get; set; } = "";
    [ObservableProperty] public partial string ElapsedText { get; set; } = "";
    [ObservableProperty] public partial string CurrentFileText { get; set; } = "";
    [ObservableProperty] public partial string RunHeading { get; set; } = "";

    // Results
    [ObservableProperty] public partial string ResultsHeading { get; set; } = "";
    [ObservableProperty] public partial string ResultsTotalsText { get; set; } = "";
    [ObservableProperty] public partial string CancelWarning { get; set; } = "";
    [ObservableProperty] public partial string ResultsError { get; set; } = "";
    [ObservableProperty] public partial bool ResultsHaveErrors { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    public partial bool CanRetry { get; set; }

    public string DryRunBanner => "Dry run: nothing was copied";

    private async Task RunAsync(bool dryRun)
    {
        var options = CurrentOptions();
        _lastRunDryRun = dryRun;
        _lastLogPath = null;
        IsDryRunActive = dryRun;
        RunHeading = dryRun ? OptionHelp.DryRunHeading : OptionHelp.CopyHeading;
        RunJobs.Clear();
        OutputLines.Clear();
        FailedItems.Clear();
        ResultsError = "";
        while (_pendingOutput.TryDequeue(out _)) { }
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        FilesText = BytesText = CurrentFileText = "";
        _runClock = Stopwatch.StartNew();
        ElapsedText = "0:00";
        Mode = ScreenMode.Running;
        _elapsedTimer.Start();
        _outputFlush.Start();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;
        IReadOnlyList<RobocopyJob> jobs = [];

        try
        {
            var logPath = _logs.ReserveLogPath(DateTime.Now, dryRun);
            _lastLogPath = logPath;
            jobs = BuildJobs(logPath, dryRun, out var buildError)
                ?? throw new InvalidOperationException(buildError ?? "The destination is not valid.");
            for (var i = 0; i < jobs.Count; i++)
                RunJobs.Add(new RunJobViewModel(i + 1, jobs[i]));

            // 1. Pre-scan
            IsScanning = true;
            ScanText = "Scanning...";
            var scanProgress = new Progress<ScanTotals>(t => ScanText = $"Scanning... {t.Files:N0} files, {Format.Bytes(t.Bytes)}");
            IReadOnlyList<ScanTotals> totals;
            try
            {
                totals = await Task.Run(() => PreScanner.Scan(jobs, options, scanProgress, ct), ct);
            }
            catch (OperationCanceledException)
            {
                ShowCancelledBeforeStart();
                return;
            }
            IsScanning = false;

            var grand = totals.Aggregate(new ScanTotals(), (a, b) => a + b);
            if (!dryRun)
            {
                var free = VolumeInfo.GetFreeBytes(jobs[0].Destination);
                var space = PreflightChecker.Check(new PreflightInput(Items.Select(i => i.Item).ToList(), Destination, options, DiskPathProbe.Instance, grand.Bytes, free));
                var low = space.Issues.FirstOrDefault(i => i.Code == PreflightCodes.LowFreeSpace);
                if (low is not null && !_prompts.Confirm("Low disk space", low.Message + "\n\nContinue anyway?", warning: true))
                {
                    ShowCancelledBeforeStart();
                    return;
                }
            }

            // 2. Log header
            var destination = PreflightChecker.ValidateDestination(Destination, DiskPathProbe.Instance, out _) ?? Destination;
            _logs.WriteHeader(logPath, new LogHeader(DateTime.Now, LogManager.AppVersion, Items.Select(i => i.Path).ToList(),
                destination, jobs.Select(j => j.CommandLine).ToList(), dryRun, IsElevated));

            // 3. Run
            IsProgressIndeterminate = false;
            UpdateProgress(new OverallProgress(0, grand.Files, 0, grand.Bytes, _runClock.Elapsed, null));
            var session = new RunSession(jobs, totals);
            session.JobStatusChanged += (i, s) => _dispatcher.BeginInvoke(() => RunJobs[i].Status = s);
            session.ProgressChanged += p => _dispatcher.BeginInvoke(() => UpdateProgress(p), DispatcherPriority.Background);
            session.OutputLine += line => _pendingOutput.Enqueue(line);

            var result = await Task.Run(() => session.RunAsync(ct));
            await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background); // drain queued progress
            ShowResults(result, options);
        }
        catch (Exception ex)
        {
            // Anything unexpected (network drop during the scan, unwritable log folder...) must not
            // leave the window stuck on the progress screen.
            ShowRunFailure(ex);
        }
        finally
        {
            _elapsedTimer.Stop();
            _outputFlush.Stop();
            FlushOutput();
            _runCts?.Dispose();
            _runCts = null;
            IsScanning = false;
            OpenLogCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateProgress(OverallProgress p)
    {
        ProgressPercent = p.Fraction * 100;
        FilesText = $"{p.FilesDone:N0} of {p.FilesTotal:N0} files";
        BytesText = $"{Format.Bytes(p.BytesDone)} of {Format.Bytes(p.BytesTotal)}";
        if (p.CurrentFile is not null)
            CurrentFileText = p.CurrentFile;
    }

    /// <summary>Moves queued lines into the panel in one batch, keeping only the last 5,000.</summary>
    private void FlushOutput()
    {
        if (_pendingOutput.IsEmpty)
            return;
        var batch = new List<string>();
        while (_pendingOutput.TryDequeue(out var line))
            batch.Add(line);
        OutputLines.AppendCapped(batch, MaxOutputLines);
        OutputAppended?.Invoke();
    }

    /// <summary>Raised after new output lines are added, so the view can keep the end in view.</summary>
    public event Action? OutputAppended;

    [RelayCommand]
    private void Cancel() => _runCts?.Cancel();

    private void ShowCancelledBeforeStart()
    {
        foreach (var j in RunJobs)
            j.Status = JobStatus.Cancelled;
        ResultsHeading = "Cancelled before copying started";
        ResultsTotalsText = "Nothing was copied.";
        CancelWarning = "";
        ResultsHaveErrors = false;
        CanRetry = true;
        Mode = ScreenMode.Results;
    }

    private void ShowRunFailure(Exception ex)
    {
        foreach (var j in RunJobs.Where(j => j.Status is JobStatus.Pending or JobStatus.Running))
        {
            j.Status = JobStatus.Failed;
            j.ResultText = "Not completed";
            j.IsError = true;
        }
        ResultsHeading = _lastRunDryRun ? "Dry run could not finish" : "Copy could not finish";
        ResultsTotalsText = "";
        ResultsError = ex.Message;
        CancelWarning = "";
        ResultsHaveErrors = true;
        CanRetry = true;
        Mode = ScreenMode.Results;
    }

    private void ShowResults(RunResult result, RobocopyOptions options)
    {
        for (var i = 0; i < RunJobs.Count; i++)
        {
            RunJobs[i].Status = result.Results[i] is null ? JobStatus.Cancelled
                : result.Results[i]!.Cancelled ? JobStatus.Cancelled
                : result.Results[i]!.Interpretation.IsSuccess ? JobStatus.Done : JobStatus.Failed;
            if (result.Results[i] is { } r)
                RunJobs[i].ApplyResult(r);
            else
                RunJobs[i].ResultText = "Not run (cancelled)";
        }

        FailedItems.Clear();
        foreach (var f in result.Failures)
            FailedItems.Add(new FailedItemViewModel(f));

        ResultsTotalsText = RunJobViewModel.CountsFor(result.Totals, result.DryRun) + $" · Elapsed {Format.Elapsed(result.Elapsed)}";
        ResultsHaveErrors = result.Cancelled || !result.AllSucceeded;
        ResultsHeading = result.Cancelled ? "Cancelled"
            : result.AllSucceeded ? (result.DryRun ? "Dry run finished" : "Copy finished")
            : (result.DryRun ? "Dry run finished with problems" : "Copy finished with problems");

        CancelWarning = result.Cancelled ? OptionHelp.CancelWarning(options, result.DryRun) : "";

        if (_lastLogPath is not null && result.Cancelled)
        {
            try
            {
                LogManager.AppendNote(_lastLogPath, "*** Cancelled by the user at " + DateTime.Now.ToString("HH:mm:ss") + " ***");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        CanRetry = result.Cancelled || !result.AllSucceeded || result.Failures.Count > 0;
        Mode = ScreenMode.Results;
    }

    [RelayCommand(CanExecute = nameof(CanOpenLog))]
    private void OpenLog()
    {
        if (_lastLogPath is null || !File.Exists(_lastLogPath))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(_lastLogPath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No program is associated with .log files; Notepad reads the UTF-16 log fine.
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + _lastLogPath + "\"") { UseShellExecute = true });
            }
            catch (Win32Exception ex)
            {
                _prompts.ShowError("Open log", $"Could not open {_lastLogPath}:\n\n{ex.Message}");
            }
        }
    }

    private bool CanOpenLog() => _lastLogPath is not null && File.Exists(_lastLogPath);

    /// <summary>Reruns the same calls; robocopy skips files that were already copied.</summary>
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryFailed() => BeginAsync(_lastRunDryRun, skipConfirmations: true);

    [RelayCommand]
    private void BackToOptions()
    {
        Mode = ScreenMode.Edit;
        Refresh();
    }

    /// <summary>Returns false if the window should stay open.</summary>
    public bool ConfirmClose()
    {
        if (Mode != ScreenMode.Running)
            return true;
        if (!_prompts.Confirm("Copy in progress", "A copy is running. Cancel it and close?", warning: true))
            return false;
        _runCts?.Cancel();
        return true;
    }
}

public sealed record ExistingModeChoice(ExistingFileMode Mode, string Name, string ToolTip);
