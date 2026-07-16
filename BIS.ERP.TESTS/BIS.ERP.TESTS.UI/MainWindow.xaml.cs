using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using BIS.ERP.Testing;
using Microsoft.Win32;

namespace BIS.ERP.TestRunner;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly SqlQueryConsoleService _sqlService = new();
    private readonly TestLogWriter _logWriter = TestLogWriter.Create("ui");
    private ScenarioCategoryViewModel? _selectedCategory;
    private ScenarioRowViewModel? _selectedScenario;
    private DatabaseCandidate? _selectedDatabase;
    private DataView? _sqlResultView;
    private string _executionLog = string.Empty;
    private string _sqlText = "select * from \"InfoBases\";";
    private string _sqlStatus = "Выберите базу, введите SQL и выполните запрос.";
    private string _cycleCount = "1";
    private string _documentCount = "1";
    private string _operationFilePath = string.Empty;
    private string _operationImportStatus = "Если файл не выбран, сценарий сгенерирует документы автоматически.";
    private string _connectionHost = "localhost";
    private string _connectionPort = "5432";
    private string _connectionDatabaseName = "bis_master";
    private string _connectionUsername = "postgres";
    private string _connectionPassword = "qwerty123";
    private string _connectionSettingsStatus = "Настройки подключения еще не проверялись.";
    private string _connectionSettingsFilePath = string.Empty;
    private string _logsDirectoryPath = string.Empty;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadConnectionSettingsFromStore();
        Loaded += MainWindow_Loaded;
    }

    public ObservableCollection<ScenarioCategoryViewModel> Categories { get; } = new();
    public ObservableCollection<DatabaseCandidate> SqlCandidates { get; } = new();
    public ObservableCollection<SmokeTestOperation> LoadedOperations { get; } = new();

    public ScenarioCategoryViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value) && value != null && SelectedScenario == null)
                SelectedScenario = value.Scenarios.FirstOrDefault();
        }
    }

    public ScenarioRowViewModel? SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (SetProperty(ref _selectedScenario, value))
                UpdateButtonState();
        }
    }

    public DatabaseCandidate? SelectedDatabase
    {
        get => _selectedDatabase;
        set => SetProperty(ref _selectedDatabase, value);
    }

    public DataView? SqlResultView
    {
        get => _sqlResultView;
        set => SetProperty(ref _sqlResultView, value);
    }

    public string ExecutionLog
    {
        get => _executionLog;
        set => SetProperty(ref _executionLog, value);
    }

    public string SqlText
    {
        get => _sqlText;
        set => SetProperty(ref _sqlText, value);
    }

    public string SqlStatus
    {
        get => _sqlStatus;
        set => SetProperty(ref _sqlStatus, value);
    }

    public string CycleCount
    {
        get => _cycleCount;
        set => SetProperty(ref _cycleCount, value);
    }

    public string DocumentCount
    {
        get => _documentCount;
        set => SetProperty(ref _documentCount, value);
    }

    public string OperationFilePath
    {
        get => _operationFilePath;
        set => SetProperty(ref _operationFilePath, value);
    }

    public string OperationImportStatus
    {
        get => _operationImportStatus;
        set => SetProperty(ref _operationImportStatus, value);
    }

    public string ConnectionHost
    {
        get => _connectionHost;
        set => SetProperty(ref _connectionHost, value);
    }

    public string ConnectionPort
    {
        get => _connectionPort;
        set => SetProperty(ref _connectionPort, value);
    }

    public string ConnectionDatabaseName
    {
        get => _connectionDatabaseName;
        set => SetProperty(ref _connectionDatabaseName, value);
    }

    public string ConnectionUsername
    {
        get => _connectionUsername;
        set => SetProperty(ref _connectionUsername, value);
    }

    public string ConnectionPassword
    {
        get => _connectionPassword;
        set => SetProperty(ref _connectionPassword, value);
    }

    public string ConnectionSettingsStatus
    {
        get => _connectionSettingsStatus;
        set => SetProperty(ref _connectionSettingsStatus, value);
    }

    public string ConnectionSettingsFilePath
    {
        get => _connectionSettingsFilePath;
        set => SetProperty(ref _connectionSettingsFilePath, value);
    }

    public string LogsDirectoryPath
    {
        get => _logsDirectoryPath;
        set => SetProperty(ref _logsDirectoryPath, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AppendLog($"Файл настроек: {ConnectionSettingsFilePath}");
        AppendLog($"Папка логов: {LogsDirectoryPath}");
        await ReloadAllAsync();
    }

    private async Task ReloadAllAsync()
    {
        PersistCurrentConnectionSettings(false);
        await LoadScenariosAsync();
        await LoadDatabasesAsync();
        UpdateButtonState();
    }

    private async Task LoadScenariosAsync()
    {
        Categories.Clear();
        var categories = SmokeTestRegistry.DiscoverScenarios()
            .GroupBy(item => item.Category)
            .OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var category in categories)
        {
            var viewModel = new ScenarioCategoryViewModel(category.Key);
            foreach (var scenario in category)
            {
                viewModel.Scenarios.Add(new ScenarioRowViewModel(scenario));
            }

            Categories.Add(viewModel);
        }

        SelectedCategory = Categories.FirstOrDefault();
        SelectedScenario = SelectedCategory?.Scenarios.FirstOrDefault();
        AppendLog($"Загружено сценариев: {Categories.Sum(item => item.Scenarios.Count)}.");
    }

    private async Task LoadDatabasesAsync()
    {
        PersistCurrentConnectionSettings(false);
        SqlCandidates.Clear();
        var progress = new Progress<string>(AppendLog);
        var candidates = await _sqlService.LoadCandidatesAsync(progress);
        foreach (var candidate in candidates)
            SqlCandidates.Add(candidate);

        SelectedDatabase = SqlCandidates.FirstOrDefault();
        SqlStatus = SelectedDatabase == null
            ? "Базы данных не найдены."
            : $"Выбрана база: {SelectedDatabase.DatabaseName}.";
    }

    private async Task RunSelectedScenarioAsync(SmokeTestCommand command)
    {
        if (SelectedScenario == null)
        {
            MessageBox.Show("Выберите сценарий теста.", "Тесты", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!SupportsCommand(SelectedScenario.Scenario, command))
        {
            MessageBox.Show("Для выбранного сценария этот режим недоступен.", "Тесты", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunScenarioAsync(SelectedScenario, command);
    }

    private async Task RunCurrentCategoryAsync(SmokeTestCommand command)
    {
        if (SelectedCategory == null)
        {
            MessageBox.Show("Выберите вкладку со сценариями.", "Тесты", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var scenario in SelectedCategory.Scenarios.Where(item => SupportsCommand(item.Scenario, command)))
        {
            await RunScenarioAsync(scenario, command);
        }
    }

    private async Task RunScenarioAsync(ScenarioRowViewModel row, SmokeTestCommand command)
    {
        SetBusy(true);
        row.StatusText = "Выполняется";
        row.LastSummary = $"{DescribeCommand(command)}...";
        AppendLog($"{row.Name}: старт {DescribeCommand(command).ToLowerInvariant()}.");

        try
        {
            PersistCurrentConnectionSettings(false);
            var progress = new Progress<string>(message => AppendLog($"{row.Name}: {message}"));
            var options = BuildRunOptions(command);
            var result = await row.Scenario.ExecuteAsync(options, progress);
            row.StatusText = result.IsSuccess ? "Успешно" : "Ошибка";
            row.LastSummary = result.Summary;

            if (result.Details.Count > 0)
            {
                foreach (var detail in result.Details)
                    AppendLog($"{row.Name}: {detail}");
            }

            AppendLog($"{row.Name}: {result.Summary}");
        }
        catch (Exception ex)
        {
            row.StatusText = "Ошибка";
            row.LastSummary = ex.Message;
            AppendLog($"{row.Name}: исключение {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private SmokeTestRunOptions BuildRunOptions(SmokeTestCommand command)
    {
        return new SmokeTestRunOptions
        {
            Command = command,
            CycleCount = ParsePositiveInt(CycleCount, 1),
            DocumentCount = ParsePositiveInt(DocumentCount, 1),
            OperationsFilePath = string.IsNullOrWhiteSpace(OperationFilePath) ? null : OperationFilePath,
            Operations = LoadedOperations.ToList()
        };
    }

    private static bool SupportsCommand(ISmokeTestScenario scenario, SmokeTestCommand command)
    {
        return command switch
        {
            SmokeTestCommand.Run => scenario.SupportsRun,
            SmokeTestCommand.Cleanup => scenario.SupportsCleanup,
            _ => scenario.SupportsVerify
        };
    }

    private async Task ExecuteSqlAsync()
    {
        if (SelectedDatabase == null)
        {
            MessageBox.Show("Выберите базу данных.", "SQL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetBusy(true);
        PersistCurrentConnectionSettings(false);
        SqlStatus = $"Выполнение SQL в базе {SelectedDatabase.DatabaseName}...";
        AppendLog($"SQL: выполнение запроса в базе {SelectedDatabase.DatabaseName}.");

        try
        {
            var result = await _sqlService.ExecuteAsync(SelectedDatabase, SqlText);
            SqlResultView = result.Table?.DefaultView;
            SqlStatus = result.Message;
            AppendLog($"SQL: {result.Message}");
        }
        catch (Exception ex)
        {
            SqlResultView = null;
            SqlStatus = $"Ошибка выполнения SQL: {ex.Message}";
            AppendLog(SqlStatus);
            MessageBox.Show(SqlStatus, "SQL", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadOperationsFromFileAsync(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        SetBusy(true);
        try
        {
            var loader = new OperationImportService();
            var operations = await loader.LoadAsync(dialog.FileName);
            LoadedOperations.Clear();
            foreach (var operation in operations)
                LoadedOperations.Add(operation);

            OperationFilePath = dialog.FileName;
            OperationImportStatus = $"Загружено операций: {LoadedOperations.Count}.";
            AppendLog(OperationImportStatus);
        }
        catch (Exception ex)
        {
            OperationImportStatus = $"Ошибка загрузки операций: {ex.Message}";
            AppendLog(OperationImportStatus);
            MessageBox.Show(OperationImportStatus, "Операции", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LoadConnectionSettingsFromStore()
    {
        var settings = TestEnvironment.LoadSettings();
        ConnectionHost = settings.Host;
        ConnectionPort = settings.Port.ToString();
        ConnectionDatabaseName = settings.DatabaseName;
        ConnectionUsername = settings.Username;
        ConnectionPassword = settings.Password;
        ConnectionSettingsFilePath = TestEnvironment.GetSettingsFilePath();
        LogsDirectoryPath = TestEnvironment.GetLogsDirectory();
        ConnectionSettingsStatus = "Настройки подключения загружены.";
    }

    private TestSettings BuildConnectionSettings()
    {
        var port = int.TryParse(ConnectionPort, out var parsedPort) && parsedPort > 0
            ? parsedPort
            : 5432;

        return new TestSettings(
            Host: string.IsNullOrWhiteSpace(ConnectionHost) ? "localhost" : ConnectionHost.Trim(),
            Port: port,
            DatabaseName: string.IsNullOrWhiteSpace(ConnectionDatabaseName) ? "bis_master" : ConnectionDatabaseName.Trim(),
            Username: string.IsNullOrWhiteSpace(ConnectionUsername) ? "postgres" : ConnectionUsername.Trim(),
            Password: ConnectionPassword ?? string.Empty);
    }

    private void PersistCurrentConnectionSettings(bool announce)
    {
        var settings = BuildConnectionSettings();
        TestEnvironment.SaveSettings(settings);
        ConnectionSettingsFilePath = TestEnvironment.GetSettingsFilePath();
        LogsDirectoryPath = TestEnvironment.GetLogsDirectory();
        ConnectionSettingsStatus = $"Настройки сохранены: {settings.Host}:{settings.Port}/{settings.DatabaseName}";

        if (announce)
            AppendLog(ConnectionSettingsStatus);
    }

    private async Task CheckConnectionAsync()
    {
        SetBusy(true);
        try
        {
            PersistCurrentConnectionSettings(false);
            var result = await _sqlService.TestConnectionAsync(BuildConnectionSettings());
            ConnectionSettingsStatus = result.Message;
            AppendLog(result.Message);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.Message, "Подключение к БД", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await LoadDatabasesAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        var canUseSelected = !_isBusy && SelectedScenario != null;
        VerifySelectedButton.IsEnabled = canUseSelected && SelectedScenario!.Scenario.SupportsVerify;
        RunSelectedButton.IsEnabled = canUseSelected && SelectedScenario!.Scenario.SupportsRun;
        CleanupSelectedButton.IsEnabled = canUseSelected && SelectedScenario!.Scenario.SupportsCleanup;
        VerifyCategoryButton.IsEnabled = !_isBusy && SelectedCategory != null;
        DatabaseComboBox.IsEnabled = !_isBusy;
        SaveConnectionSettingsButton.IsEnabled = !_isBusy;
        TestConnectionSettingsButton.IsEnabled = !_isBusy;
    }

    private void AppendLog(string message)
    {
        var entry = TestEnvironment.FormatLogEntry(message);
        var builder = new StringBuilder(ExecutionLog);
        if (builder.Length > 0)
            builder.AppendLine();

        builder.Append(entry);

        ExecutionLog = builder.ToString();
        _logWriter.WriteLine(entry);
        ExecutionLogTextBox.ScrollToEnd();
    }

    private static string DescribeCommand(SmokeTestCommand command)
    {
        return command switch
        {
            SmokeTestCommand.Run => "Оставить данные",
            SmokeTestCommand.Cleanup => "Очистить",
            _ => "Проверить"
        };
    }

    private static int ParsePositiveInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadConnectionSettingsFromStore();
        await ReloadAllAsync();
    }

    private async void VerifySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSelectedScenarioAsync(SmokeTestCommand.Verify);
    }

    private async void RunSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSelectedScenarioAsync(SmokeTestCommand.Run);
    }

    private async void CleanupSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSelectedScenarioAsync(SmokeTestCommand.Cleanup);
    }

    private async void VerifyCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        await RunCurrentCategoryAsync(SmokeTestCommand.Verify);
    }

    private async void RefreshDatabasesButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadDatabasesAsync();
    }

    private async void ExecuteSqlButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteSqlAsync();
    }

    private async void LoadJsonButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadOperationsFromFileAsync("JSON (*.json)|*.json");
    }

    private async void LoadExcelButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadOperationsFromFileAsync("Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm");
    }

    private void ClearOperationsButton_Click(object sender, RoutedEventArgs e)
    {
        LoadedOperations.Clear();
        OperationFilePath = string.Empty;
        OperationImportStatus = "Файл операций очищен. Будет использована автогенерация документов.";
        AppendLog(OperationImportStatus);
    }

    private async void SaveConnectionSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        PersistCurrentConnectionSettings(true);
        await LoadDatabasesAsync();
    }

    private async void TestConnectionSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckConnectionAsync();
    }
}

public sealed class ScenarioCategoryViewModel
{
    public ScenarioCategoryViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public ObservableCollection<ScenarioRowViewModel> Scenarios { get; } = new();
}

public sealed class ScenarioRowViewModel : INotifyPropertyChanged
{
    private string _statusText = "Готов";
    private string _lastSummary = "Еще не запускался";

    public ScenarioRowViewModel(ISmokeTestScenario scenario)
    {
        Scenario = scenario;
    }

    public ISmokeTestScenario Scenario { get; }
    public string Code => Scenario.Code;
    public string Name => Scenario.Name;
    public string Description => Scenario.Description;

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
                return;

            _statusText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public string LastSummary
    {
        get => _lastSummary;
        set
        {
            if (_lastSummary == value)
                return;

            _lastSummary = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSummary)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
