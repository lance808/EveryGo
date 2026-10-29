using System;
using System.Drawing;
using System.Windows.Forms;

namespace EveryGo
{
    /// <summary>仅在 closeAction = tray 时创建的托盘图标与菜单。</summary>
    internal sealed class TrayService : IDisposable
    {
        private readonly MainWindow _main;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly Icon _ownedIcon;

        public TrayService(MainWindow main, string exePath)
        {
            _main = main;

            Icon icon = null;
            try
            {
                if (!string.IsNullOrEmpty(exePath))
                {
                    icon = Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("提取托盘图标失败，使用系统图标：" + ex.Message);
            }
            if (icon == null)
            {
                icon = SystemIcons.Application;
            }
            else
            {
                _ownedIcon = icon;
            }

            _menu = new ContextMenuStrip();
            _menu.Items.Add("显示面板", null, delegate { _main.ShowPanel(); });
            _menu.Items.Add("设置", null, delegate
            {
                _main.ShowPanel();
                _main.OpenSettings();
            });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("退出", null, delegate { _main.ExitApplication(); });

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "EveryGo",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _notifyIcon.DoubleClick += delegate { _main.ShowPanel(); };
            Logger.Info("托盘图标已创建");
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            if (_menu != null) _menu.Dispose();
            if (_ownedIcon != null) _ownedIcon.Dispose();
            Logger.Info("托盘图标已释放");
        }
    }
}
