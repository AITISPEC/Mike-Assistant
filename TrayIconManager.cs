using System;
using System.Windows;
using System.Windows.Forms;
using System.Drawing;

namespace MikeAssistant
{
    public class TrayIconManager : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private Window _mainWindow;
        private Icon _idleIcon;
        private Icon _activeIcon;

        public TrayIconManager(Window mainWindow)
        {
            _mainWindow = mainWindow;

            // Создаём иконки (можно заменить на свои .ico файлы)
            _idleIcon = SystemIcons.Information;
            _activeIcon = SystemIcons.Application;

            CreateTrayIcon();
        }

        private void CreateTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = _idleIcon,
                Text = "Майк — ожидание",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Показать", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Выход", null, (s, e) => Exit());
            _notifyIcon.ContextMenuStrip = contextMenu;

            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        }

        public void SetActiveMode(bool isActive)
        {
            if (isActive)
            {
                _notifyIcon.Icon = _activeIcon;
                _notifyIcon.Text = "Майк — активен";
            }
            else
            {
                _notifyIcon.Icon = _idleIcon;
                _notifyIcon.Text = "Майк — ожидание";
            }
        }

        public void ShowWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        public void HideWindow()
        {
            _mainWindow.Hide();
        }

        private void Exit()
        {
            _notifyIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
    }
}