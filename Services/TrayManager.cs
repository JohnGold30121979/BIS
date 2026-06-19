using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace BIS.ERP.Services
{    
    // Менеджер для управления иконкой в системном трее
    public class TrayManager : IDisposable
    {
        private NotifyIcon? _trayIcon;
        private Window? _mainWindow;
        private bool _disposed;

       
        // Событие при двойном клике по иконке
        public event EventHandler? DoubleClick;
        
        // Событие при выборе пункта "Показать"
        public event EventHandler? ShowRequested;
       
        // Событие при выборе пункта "Выход"
        public event EventHandler? ExitRequested;

        public TrayManager()
        {
            InitializeTrayIcon();
        }

        public TrayManager(Window mainWindow) : this()
        {
            _mainWindow = mainWindow;
        }

        
        // Инициализация иконки в системном трее
        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon();

            // Загрузка иконки из папки Assets
            LoadIcon();

            // Настройка подсказки
            _trayIcon.Text = "BIS ERP";

            // Контекстное меню
            _trayIcon.ContextMenuStrip = CreateContextMenu();

            // Обработчики событий
            _trayIcon.DoubleClick += OnDoubleClick;
            _trayIcon.MouseClick += OnMouseClick;

            // Показываем иконку
            _trayIcon.Visible = true;
        }

        // Загрузка иконки из папки Assets
        private void LoadIcon()
        {
            if (_trayIcon == null) return;

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");

            try
            {
                if (File.Exists(iconPath))
                {
                    _trayIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    _trayIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
        }

        // Создание контекстного меню       
        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            // Пункт "Показать приложение"
            var showItem = new ToolStripMenuItem("Показать");
            showItem.Click += (s, e) => OnShowRequested();
            showItem.Image = SystemIcons.Application.ToBitmap();
            menu.Items.Add(showItem);

            // Разделитель
            menu.Items.Add(new ToolStripSeparator());

            // Пункт "Выход"
            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += (s, e) => OnExitRequested();
            exitItem.Image = SystemIcons.Shield.ToBitmap();
            menu.Items.Add(exitItem);

            return menu;
        }
       
        // Обработчик двойного клика        
        private void OnDoubleClick(object? sender, EventArgs e)
        {
            DoubleClick?.Invoke(this, e);

            // Если есть главное окно - показываем/скрываем его
            if (_mainWindow != null)
            {
                ToggleMainWindow();
            }
        }
       
        // Обработчик клика по иконке       
        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            // Показываем меню при клике правой кнопкой
            if (e.Button == MouseButtons.Right && _trayIcon?.ContextMenuStrip != null)
            {
                _trayIcon.ContextMenuStrip.Show(Cursor.Position);
            }
        }
       
        // Показать/скрыть главное окно
        private void ToggleMainWindow()
        {
            if (_mainWindow == null) return;

            if (_mainWindow.Visibility == Visibility.Visible && _mainWindow.WindowState != WindowState.Minimized)
            {
                // Если окно видимо и не свернуто - скрываем его
                _mainWindow.Hide();
                ShowBalloonTip("Приложение свернуто в трей", ToolTipIcon.Info);
            }
            else
            {
                // Иначе показываем
                ShowMainWindow();
            }
        }
       
        // Показать главное окно
        private void ShowMainWindow()
        {
            if (_mainWindow == null) return;

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
        }
        
        // Показать всплывающую подсказку
        public void ShowBalloonTip(string text, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 1000)
        {
            if (_trayIcon?.Visible == true)
            {
                _trayIcon.ShowBalloonTip(timeout, "BIS ERP", text, icon);
            }
        }
       
        // Показать всплывающую подсказку с заголовком
        public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 1000)
        {
            if (_trayIcon?.Visible == true)
            {
                _trayIcon.ShowBalloonTip(timeout, title, text, icon);
            }
        }
        
        // Обновить текст подсказки
        public void UpdateTooltip(string text)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Text = text;
            }
        }
        
        // Обновить иконку
        public void UpdateIcon(string iconPath)
        {
            if (_trayIcon == null) return;

            try
            {
                if (File.Exists(iconPath))
                {
                    _trayIcon.Icon = new Icon(iconPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обновления иконки: {ex.Message}");
            }
        }
       
        // Скрыть иконку из трея
        public void Hide()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
            }
        }
        
        // Показать иконку в трее
        public void Show()
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
            }
        }

        #region Event Invokers

        private void OnShowRequested()
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
            ShowMainWindow();
        }

        private void OnExitRequested()
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.DoubleClick -= OnDoubleClick;
                        _trayIcon.MouseClick -= OnMouseClick;
                        _trayIcon.Visible = false;
                        _trayIcon.Dispose();
                        _trayIcon = null;
                    }
                }
                _disposed = true;
            }
        }

        #endregion

        private Icon CreateIconWith16x16(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    // Загружаем иконку
                    using (var icon = new Icon(stream))
                    {
                        // Проверяем, есть ли 16x16
                        if (icon.Width == 16 && icon.Height == 16)
                        {
                            return new Icon(icon, 16, 16);
                        }

                        // Если нет 16x16 - создаем из 64x64
                        using (var bitmap = icon.ToBitmap())
                        {
                            // Создаем уменьшенную копию 16x16
                            using (var smallBitmap = new Bitmap(16, 16))
                            {
                                using (var g = Graphics.FromImage(smallBitmap))
                                {
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    g.DrawImage(bitmap, 0, 0, 16, 16);
                                }

                                // Конвертируем в иконку
                                var hIcon = smallBitmap.GetHicon();
                                return Icon.FromHandle(hIcon);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания иконки: {ex.Message}");
                return SystemIcons.Application;
            }
        }
    }
}