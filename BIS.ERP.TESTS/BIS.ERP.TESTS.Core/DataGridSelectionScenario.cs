using System.Collections.ObjectModel;
using System.Data;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using BIS.ERP.Views;

namespace BIS.ERP.Testing;

/// <summary>
/// Проверяет, что после закрытия диалога создания/редактирования записи строка
/// остаётся выделенной и таблица получает клавиатурный фокус, даже если список
/// перезагружается повторно (MDI заново прикрепляет вкладку-вызыватель),
/// а модальные сообщения «Запись добавлена» забирают фокус.
/// </summary>
public sealed class DataGridSelectionScenario : SmokeTestScenarioBase
{
    public override string Code => "ui-grid-selection";
    public override string Name => "Выделение строки после диалога";
    public override string Category => "Интерфейс";
    public override string Description =>
        "Проверка удержания выделения и клавиатурного фокуса в таблице после закрытия диалога записи.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override Task<SmokeTestResult> ExecuteAsync(
        SmokeTestRunOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                RunChecks(details, progress);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(60)))
            return Task.FromResult(SmokeTestResult.Failure("Проверка не завершилась за 60 секунд.", details));

        if (failure != null)
            return Task.FromResult(SmokeTestResult.Failure(failure.Message, details));

        return Task.FromResult(
            SmokeTestResult.Success("Выделение строки и фокус в таблице после диалога сохраняются.", details.ToArray()));
    }

    private static void RunChecks(List<string> details, IProgress<string>? progress)
    {
        var problems = new List<string>();
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var window = new Window
        {
            Title = "BIS.ERP: проверка выделения строки",
            Width = 720,
            Height = 360,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000
        };

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Наименование",
            Binding = new Binding(nameof(TestRow.Name))
        });

        window.Content = grid;
        window.Show();
        Pump(TimeSpan.FromMilliseconds(150));

        grid.ItemsSource = new ObservableCollection<TestRow>
        {
            new(otherId, "Первая запись"),
            new(targetId, "Новая запись")
        };

        // 1. Немедленный отклик: строка выделяется сразу после закрытия диалога.
        grid.SelectRowById(targetId, item => item is TestRow row ? row.Id : null);
        Check(
            grid.SelectedItem is TestRow immediate && immediate.Id == targetId,
            "новая запись выделена сразу после вызова",
            problems,
            details);

        // 2. Список перезагружается повторно — так делает Loaded вкладки-вызывателя
        //    после закрытия диалога в MDI. Выделение не должно потеряться.
        var reloaded = new ObservableCollection<TestRow> { new(otherId, "Первая запись") };
        grid.ItemsSource = reloaded;
        Pump(TimeSpan.FromMilliseconds(250));

        reloaded.Add(new TestRow(targetId, "Новая запись"));
        Pump(TimeSpan.FromMilliseconds(600));

        Check(
            grid.SelectedItem is TestRow afterReload && afterReload.Id == targetId,
            "выделение восстановлено после повторной загрузки списка",
            problems,
            details);

        // 3. Выбор пользователя не перебивается восстановлением выделения.
        grid.SelectedItem = reloaded[0];
        Pump(TimeSpan.FromMilliseconds(500));

        Check(
            grid.SelectedItem is TestRow userRow && userRow.Id == otherId,
            "выбор пользователя после закрытия диалога не перебивается",
            problems,
            details);

        if (window.IsActive)
        {
            Check(grid.IsKeyboardFocusWithin, "клавиатурный фокус переведён в таблицу", problems, details);
        }
        else
        {
            progress?.Report("Окно теста не активно — проверка клавиатурного фокуса пропущена.");
            details.Add("(пропущено) клавиатурный фокус: окно теста не активно");
        }

        // 4. Разбор идентификатора строки не падает на пустых данных.
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Наименование", typeof(string));
        table.Rows.Add(targetId, "Запись");

        Check(
            DataGridSelectionHelper.GetIdFromDataRow(table.DefaultView[0]) == targetId,
            "GetIdFromDataRow возвращает идентификатор записи",
            problems,
            details);
        Check(
            DataGridSelectionHelper.GetIdFromDataRow(null) == null,
            "GetIdFromDataRow на пустой строке возвращает null",
            problems,
            details);
        Check(
            DataGridSelectionHelper.GetIdFromDictionary(null) == null,
            "GetIdFromDictionary на пустой строке возвращает null",
            problems,
            details);

        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));

        if (problems.Count > 0)
            throw new InvalidOperationException("Не выполнено: " + string.Join("; ", problems));
    }

    /// <summary>Прокачивает очередь диспетчера указанное время (нужно для DispatcherTimer).</summary>
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Check(
        bool condition,
        string description,
        List<string> problems,
        List<string> details)
    {
        if (condition)
        {
            details.Add("ок: " + description);
            return;
        }

        problems.Add(description);
        details.Add("ошибка: " + description);
    }

    private sealed class TestRow
    {
        public TestRow(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public Guid Id { get; }

        public string Name { get; }
    }
}
