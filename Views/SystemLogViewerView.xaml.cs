using BIS.ERP.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class SystemLogViewerView : UserControl, INotifyPropertyChanged
    {
        private LogFileItem? _selectedLogFile;
        private string _fileText = string.Empty;
        private string _selectedFileName = "Файл не выбран";
        private string _selectedFilePath = SystemLogService.LogDirectory;
        private string _statusText = "Файлы логов не загружены.";

        public ObservableCollection<LogFileItem> LogFiles { get; } = new();

        public LogFileItem? SelectedLogFile
        {
            get => _selectedLogFile;
            set
            {
                if (Equals(_selectedLogFile, value))
                    return;

                _selectedLogFile = value;
                OnPropertyChanged();
                LoadSelectedFile();
            }
        }

        public string FileText
        {
            get => _fileText;
            private set
            {
                _fileText = value;
                OnPropertyChanged();
            }
        }

        public string SelectedFileName
        {
            get => _selectedFileName;
            private set
            {
                _selectedFileName = value;
                OnPropertyChanged();
            }
        }

        public string SelectedFilePath
        {
            get => _selectedFilePath;
            private set
            {
                _selectedFilePath = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public SystemLogViewerView()
        {
            InitializeComponent();
            DataContext = this;
            LoadLogFiles();
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            LoadLogFiles();
        }

        private void OnLogFileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogFilesList.SelectedItem is LogFileItem item)
                SelectedLogFile = item;
        }

        private void LoadLogFiles()
        {
            var previousPath = SelectedLogFile?.FullPath;
            LogFiles.Clear();

            try
            {
                Directory.CreateDirectory(SystemLogService.LogDirectory);
                var files = Directory
                    .EnumerateFiles(SystemLogService.LogDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(path => IsLogFile(path))
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTime)
                    .Select(file => new LogFileItem(file))
                    .ToList();

                foreach (var file in files)
                    LogFiles.Add(file);

                SelectedLogFile = LogFiles.FirstOrDefault(file => file.FullPath.Equals(previousPath, StringComparison.OrdinalIgnoreCase))
                    ?? LogFiles.FirstOrDefault();

                if (SelectedLogFile == null)
                {
                    FileText = string.Empty;
                    SelectedFileName = "Логи пока не найдены";
                    SelectedFilePath = SystemLogService.LogDirectory;
                    StatusText = $"Папка логов: {SystemLogService.LogDirectory}";
                }
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка загрузки списка логов.", nameof(SystemLogViewerView), ex);
                FileText = string.Empty;
                SelectedFileName = "Ошибка чтения логов";
                SelectedFilePath = SystemLogService.LogDirectory;
                StatusText = ex.Message;
            }
        }

        private void LoadSelectedFile()
        {
            if (SelectedLogFile == null)
                return;

            try
            {
                var text = ReadAllTextShared(SelectedLogFile.FullPath);
                var info = new FileInfo(SelectedLogFile.FullPath);
                var lineCount = CountLines(text);

                FileText = text;
                SelectedFileName = SelectedLogFile.DisplayName;
                SelectedFilePath = SelectedLogFile.FullPath;
                StatusText = $"Строк: {lineCount:N0} | Размер: {FormatBytes(info.Length)} | Изменен: {info.LastWriteTime:dd.MM.yyyy HH:mm:ss}";
            }
            catch (Exception ex)
            {
                SystemLogService.Error($"Ошибка чтения лога {SelectedLogFile.FullPath}.", nameof(SystemLogViewerView), ex);
                FileText = string.Empty;
                SelectedFileName = SelectedLogFile.DisplayName;
                SelectedFilePath = SelectedLogFile.FullPath;
                StatusText = $"Ошибка чтения файла: {ex.Message}";
            }
        }

        private static bool IsLogFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadAllTextShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var count = text.Count(character => character == '\n');
            return text.EndsWith('\n') ? count : count + 1;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ" };
            var value = (double)bytes;
            var unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class LogFileItem
    {
        public LogFileItem(FileInfo file)
        {
            FullPath = file.FullName;
            DisplayName = file.Name;
            Info = $"{file.LastWriteTime:dd.MM.yyyy HH:mm:ss} | {FormatBytes(file.Length)}";
        }

        public string FullPath { get; }

        public string DisplayName { get; }

        public string Info { get; }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ" };
            var value = (double)bytes;
            var unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }
    }
}
