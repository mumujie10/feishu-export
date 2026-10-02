using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FeishuDocExport.Gui.Services;
using FeishuDocExport.Models;

namespace FeishuDocExport.Gui.ViewModels;

/// <summary>
/// 主窗口的 ViewModel：负责参数收集、调用导出服务、把进度反馈到界面。
/// 所有耗时操作都在后台线程上完成，界面不会卡住。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private const int MaxLogEntries = 3000;

    private readonly DispatcherTimer _settingsSaveTimer;
    private AppSettings _settings;
    private Window? _window;
    private CancellationTokenSource? _cts;
    private bool _loadingSettings;
    private bool _syncingSpaceSelection;

    #region 应用凭证

    [ObservableProperty]
    private string _appId = string.Empty;

    [ObservableProperty]
    private string _appSecret = string.Empty;

    [ObservableProperty]
    private bool _rememberAppSecret = true;

    /// <summary>0=飞书（国内），1=Lark（国际），2=自定义。</summary>
    [ObservableProperty]
    private int _endpointIndex;

    [ObservableProperty]
    private string _baseUrl = FeishuEndpoints.DefaultOpenApi;

    #endregion

    #region 导出对象

    [ObservableProperty]
    private bool _isWikiSource = true;

    [ObservableProperty]
    private string _wikiSpaceId = string.Empty;

    [ObservableProperty]
    private WikiSpace? _selectedWikiSpace;

    [ObservableProperty]
    private string _folderToken = string.Empty;

    public ObservableCollection<WikiSpace> WikiSpaces { get; } = new();

    #endregion

    #region 导出设置

    [ObservableProperty]
    private string _exportPath = string.Empty;

    /// <summary>0=docx，1=pdf，2=md。</summary>
    [ObservableProperty]
    private int _formatIndex;

    [ObservableProperty]
    private string _licensePath = string.Empty;

    [ObservableProperty]
    private bool _skipExistingFiles;

    #endregion

    #region 运行状态

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "就绪，等待开始。";

    [ObservableProperty]
    private string _statusText = "就绪。";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _failureSummary = "尚未开始导出。";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _licenseStatusText = string.Empty;

    [ObservableProperty]
    private bool _isLicenseOk;

    /// <summary>0=跟随系统，1=浅色，2=深色。</summary>
    [ObservableProperty]
    private int _themeMode;

    /// <summary>由视图注入：弹出「选择目录」对话框。</summary>
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>由视图注入：弹出「选择许可证文件」对话框。</summary>
    public Func<Task<string?>>? PickLicenseFileAsync { get; set; }

    /// <summary>由视图注入：打开引导窗口。参数是要看的章节 Id，null 表示从头浏览完整向导。</summary>
    public Func<string?, Task>? ShowGuideAsync { get; set; }

    /// <summary>还没走完首启向导时为 true，视图据此在启动时自动弹出向导。</summary>
    public bool OnboardingPending => !_settings.OnboardingSeen;

    public ObservableCollection<LogEntry> Logs { get; } = new();

    public ObservableCollection<FailureItem> Failures { get; } = new();

    /// <summary>日志区是否有内容，用来切换空状态提示。</summary>
    public bool HasLogs => Logs.Count > 0;

    /// <summary>未导出清单是否有内容。</summary>
    public bool HasFailures => Failures.Count > 0;

    /// <summary>是否已经跑出结果摘要，没开始时不占一行空白。</summary>
    public bool HasSummary => !string.IsNullOrEmpty(SummaryText);

    public bool IsCloudDocSource
    {
        get => !IsWikiSource;
        set
        {
            if (value)
            {
                IsWikiSource = false;
            }
        }
    }

    public bool IsCustomEndpoint => EndpointIndex == 2;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    #endregion

    public MainWindowViewModel()
    {
        _loadingSettings = true;
        _settings = SettingsStore.Load();
        ApplySettings(_settings);
        _loadingSettings = false;

        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            PersistSettings();
        };

        UpdateLicenseStatus();

        Logs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasLogs));
        Failures.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFailures));
    }

    /// <summary>绑定宿主窗口，注入文件选择器等与界面相关的能力。</summary>
    public void AttachWindow(Window window)
    {
        _window = window;

        PickFolderAsync = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(window);
            if (topLevel is null)
            {
                return null;
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择文档导出的目录",
                AllowMultiple = false,
            });

            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        };

        PickLicenseFileAsync = async () =>
        {
            var topLevel = TopLevel.GetTopLevel(window);
            if (topLevel is null)
            {
                return null;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 Aspose.Words 许可证文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("许可证文件") { Patterns = new[] { "*.lic", "*.xml" } },
                    FilePickerFileTypes.All,
                },
            });

            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };

        window.Closing += (_, _) => PersistSettings();
    }

    #region 命令

    [RelayCommand]
    private async Task BrowseExportPathAsync()
    {
        var picked = PickFolderAsync is null ? null : await PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            ExportPath = picked;
            PersistSettings();
        }
    }

    [RelayCommand]
    private async Task BrowseLicenseAsync()
    {
        var picked = PickLicenseFileAsync is null ? null : await PickLicenseFileAsync();
        if (!string.IsNullOrWhiteSpace(picked))
        {
            LicensePath = picked;
            UpdateLicenseStatus();
        }
    }

    [RelayCommand]
    private async Task LoadSpacesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();

        var options = BuildOptions();
        if (string.IsNullOrWhiteSpace(options.AppId) || string.IsNullOrWhiteSpace(options.AppSecret))
        {
            ShowError("请先填写 AppId 和 AppSecret。");
            return;
        }

        IsBusy = true;
        StatusText = "正在获取知识库列表…";

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var client = new FeishuApiClient(options);
            var service = new ExportService(client);
            var spaces = await service.GetWikiSpacesAsync(timeout.Token);

            WikiSpaces.Clear();
            foreach (var space in spaces)
            {
                WikiSpaces.Add(space);
            }

            if (WikiSpaces.Count == 0)
            {
                ShowError("当前应用没有任何可导出的知识库，请检查应用权限与知识库成员设置。");
                return;
            }

            SelectedWikiSpace = WikiSpaces.FirstOrDefault(s => s.SpaceId == WikiSpaceId) ?? WikiSpaces[0];
            StatusText = $"已获取 {WikiSpaces.Count} 个知识库。";
        }
        catch (OperationCanceledException)
        {
            ShowError("获取知识库列表超时，请检查网络与开放平台地址。");
        }
        catch (FeishuApiException ex)
        {
            ShowError(ex.ToUserMessage());
        }
        catch (Exception ex)
        {
            ShowError($"获取知识库列表失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearError();
        PersistSettings();

        var options = BuildOptions();
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            ShowError(string.Join(Environment.NewLine, errors));
            return;
        }

        Logs.Clear();
        Failures.Clear();
        ProgressValue = 0;
        SummaryText = string.Empty;
        FailureSummary = "导出进行中…";
        IsBusy = true;
        _cts = new CancellationTokenSource();

        var progress = new Progress<ExportProgress>(OnProgress);

        try
        {
            using var client = new FeishuApiClient(options);
            var service = new ExportService(client);

            AddLog(ExportLogLevel.Info, $"开始导出：{DescribeTarget(options)}");

            var result = await service.RunAsync(options, progress, _cts.Token);
            OnFinished(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消。";
            SummaryText = "导出已取消。";
            FailureSummary = Failures.Count == 0 ? "导出已取消，没有记录到失败文档。" : $"导出已取消，{Failures.Count} 篇未完成。";
            AddLog(ExportLogLevel.Warning, "导出已被取消。");
        }
        catch (FeishuApiException ex)
        {
            ShowError(ex.ToUserMessage());
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"导出失败：{ex.Message}");
            AddLog(ExportLogLevel.Error, ex.ToString());
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts is null || _cts.IsCancellationRequested)
        {
            return;
        }

        StatusText = "正在取消…";
        _cts.Cancel();
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        var path = ExportPath?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowError("还没有设置导出目录。");
            return;
        }

        if (!ShellHelper.TryOpenFolder(path, out var error))
        {
            ShowError($"无法打开目录：{error}");
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        StatusText = "日志已清空。";
    }

    /// <summary>打开使用引导。参数为章节 Id（AppId、知识库这些输入框旁的「?」会传），留空表示浏览全部章节。</summary>
    [RelayCommand]
    private async Task OpenGuideAsync(string? stepId)
    {
        if (ShowGuideAsync is { } handler)
        {
            await handler(stepId);
        }
    }

    #endregion

    #region 内部逻辑

    private ExportOptions BuildOptions() => new()
    {
        AppId = AppId?.Trim() ?? string.Empty,
        AppSecret = AppSecret?.Trim() ?? string.Empty,
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? FeishuEndpoints.DefaultOpenApi : BaseUrl.Trim(),
        ExportPath = ExportPath?.Trim() ?? string.Empty,
        SourceType = IsWikiSource ? DocSourceType.Wiki : DocSourceType.CloudDoc,
        WikiSpaceId = IsWikiSource ? NullIfEmpty(WikiSpaceId) : null,
        FolderToken = IsWikiSource ? null : NullIfEmpty(FolderToken),
        Format = FormatIndex switch
        {
            1 => ExportFormat.Pdf,
            2 => ExportFormat.Markdown,
            _ => ExportFormat.Docx,
        },
        AsposeLicensePath = NullIfEmpty(LicensePath),
        SkipExistingFiles = SkipExistingFiles,
    };

    private static string DescribeTarget(ExportOptions options)
        => options.SourceType == DocSourceType.Wiki
            ? $"知识库 {options.WikiSpaceId}"
            : $"个人空间文件夹 {options.FolderToken}";

    private void OnProgress(ExportProgress progress)
    {
        if (progress.Current is > 0 && progress.Total is > 0)
        {
            ProgressValue = progress.Current.Value * 100.0 / progress.Total.Value;
        }

        if (!string.IsNullOrWhiteSpace(progress.CurrentDocument) &&
            progress.Current is > 0 && progress.Total is > 0)
        {
            ProgressText = $"{progress.Current}/{progress.Total}　{progress.CurrentDocument}";
        }
        else
        {
            ProgressText = progress.Message;
        }

        StatusText = progress.Message;
        AddLog(progress.Level, progress.Message);
    }

    private void OnFinished(ExportResult result)
    {
        SummaryText = result.Canceled
            ? $"已取消：成功 {result.Succeeded}/{result.Total}，耗时 {FormatDuration(result.Elapsed)}"
            : $"导出完成：成功 {result.Succeeded}/{result.Total}，耗时 {FormatDuration(result.Elapsed)}";

        ProgressValue = result.Total == 0 ? 0 : result.Succeeded * 100.0 / result.Total;
        ProgressText = SummaryText;
        StatusText = SummaryText;

        foreach (var failure in result.Failures)
        {
            Failures.Add(new FailureItem(failure.Title, failure.Reason, failure.IsSkipped));
        }

        FailureSummary = result.Failures.Count == 0
            ? "全部文档导出成功，没有需要手动处理的文档。"
            : $"共有 {result.Failures.Count} 篇文档未能导出，请手动处理：";

        AddLog(
            result.Failures.Count == 0 ? ExportLogLevel.Success : ExportLogLevel.Warning,
            FailureSummary);
    }

    private static string FormatDuration(TimeSpan elapsed)
        => elapsed.TotalMinutes >= 1
            ? $"{elapsed.TotalMinutes:0.#} 分钟"
            : $"{elapsed.TotalSeconds:0} 秒";

    private void AddLog(ExportLogLevel level, string message)
    {
        Logs.Add(new LogEntry(DateTime.Now, level, message));

        while (Logs.Count > MaxLogEntries)
        {
            Logs.RemoveAt(0);
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        StatusText = message;
        AddLog(ExportLogLevel.Error, message);
    }

    private void ClearError() => ErrorMessage = string.Empty;

    private void UpdateLicenseStatus()
    {
        var status = MarkdownConverter.InitializeLicense(NullIfEmpty(LicensePath));
        LicenseStatusText = status.Message;
        IsLicenseOk = status.IsLicensed;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    #endregion

    #region 设置持久化

    private void ApplySettings(AppSettings settings)
    {
        AppId = settings.AppId;
        AppSecret = settings.AppSecret;
        RememberAppSecret = settings.RememberAppSecret;

        EndpointIndex = settings.EndpointIndex is >= 0 and <= 2 ? settings.EndpointIndex : 0;
        BaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? FeishuEndpoints.DefaultOpenApi : settings.BaseUrl;

        IsWikiSource = settings.IsWikiSource;
        WikiSpaceId = settings.WikiSpaceId;
        FolderToken = settings.FolderToken;
        ExportPath = settings.ExportPath;
        FormatIndex = settings.FormatIndex is >= 0 and <= 2 ? settings.FormatIndex : 0;
        LicensePath = settings.LicensePath;
        SkipExistingFiles = settings.SkipExistingFiles;
        ThemeMode = settings.ThemeMode is >= 0 and <= 2 ? settings.ThemeMode : 0;

        // 主题在加载设置时立刻应用，避免界面先闪一下系统配色
        ThemeManager.Apply(ThemeMode);
    }

    private AppSettings CollectSettings() => new()
    {
        AppId = AppId ?? string.Empty,
        AppSecret = RememberAppSecret ? AppSecret ?? string.Empty : string.Empty,
        RememberAppSecret = RememberAppSecret,
        BaseUrl = BaseUrl ?? FeishuEndpoints.DefaultOpenApi,
        EndpointIndex = EndpointIndex,
        IsWikiSource = IsWikiSource,
        WikiSpaceId = WikiSpaceId ?? string.Empty,
        FolderToken = FolderToken ?? string.Empty,
        ExportPath = ExportPath ?? string.Empty,
        FormatIndex = FormatIndex,
        LicensePath = LicensePath ?? string.Empty,
        SkipExistingFiles = SkipExistingFiles,
        ThemeMode = ThemeMode,
        OnboardingSeen = _settings.OnboardingSeen,
    };

    private void PersistSettings()
    {
        if (_loadingSettings)
        {
            return;
        }

        _settings = CollectSettings();
        SettingsStore.Save(_settings);
    }

    /// <summary>完整走完首启向导后调用，之后启动不再自动弹出。</summary>
    public void CompleteOnboarding()
    {
        _settings.OnboardingSeen = true;
        SettingsStore.Save(_settings);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_loadingSettings)
        {
            return;
        }

        // 输入框每敲一个字符都会触发，做个防抖，避免频繁写磁盘
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    partial void OnIsWikiSourceChanged(bool value)
        => OnPropertyChanged(nameof(IsCloudDocSource));

    partial void OnEndpointIndexChanged(int value)
    {
        if (!_loadingSettings)
        {
            BaseUrl = value switch
            {
                1 => FeishuEndpoints.LarkOpenApi,
                2 => BaseUrl,
                _ => FeishuEndpoints.DefaultOpenApi,
            };
        }

        OnPropertyChanged(nameof(IsCustomEndpoint));
    }

    partial void OnErrorMessageChanged(string value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnSummaryTextChanged(string value)
        => OnPropertyChanged(nameof(HasSummary));

    partial void OnSelectedWikiSpaceChanged(WikiSpace? value)
    {
        if (value is null || _syncingSpaceSelection)
        {
            return;
        }

        _syncingSpaceSelection = true;
        WikiSpaceId = value.SpaceId ?? string.Empty;
        _syncingSpaceSelection = false;
    }

    partial void OnWikiSpaceIdChanged(string value)
    {
        if (_syncingSpaceSelection || WikiSpaces.Count == 0)
        {
            return;
        }

        if (SelectedWikiSpace?.SpaceId == value)
        {
            return;
        }

        var matched = WikiSpaces.FirstOrDefault(s => s.SpaceId == value);
        if (matched is not null)
        {
            _syncingSpaceSelection = true;
            SelectedWikiSpace = matched;
            _syncingSpaceSelection = false;
        }
    }

    partial void OnLicensePathChanged(string value) => UpdateLicenseStatus();

    partial void OnThemeModeChanged(int value) => ThemeManager.Apply(value);

    #endregion
}
