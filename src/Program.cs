using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace EveryGo
{
    internal static class Program
    {
        public const string AppVersion = "0.3.0";
        public const string PhaseLabel = "Phase 3";

        private const string MutexName = "EveryGo.SingleInstance.v1";

        [STAThread]
        private static int Main(string[] args)
        {
            if (IsSelfTest(args))
            {
                return SelfTest.Run();
            }

            bool createdNew = false;
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (Exception)
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                ActivateExistingInstance();
                if (mutex != null) mutex.Dispose();
                return 0;
            }

            try
            {
                return RunApplication();
            }
            finally
            {
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    mutex.Dispose();
                }
            }
        }

        private static bool IsSelfTest(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int RunApplication()
        {
            AppPaths paths = AppPaths.Resolve();
            Logger.Initialize(paths.LogFile);
            IconCache.Initialize(paths.IconsDir);
            Logger.Info("启动 EveryGo " + AppVersion + "（" + PhaseLabel + "），数据目录：" + paths.DataDir
                + "，便携：" + paths.IsPortable);

            ConfigStore store = new ConfigStore(paths);
            Config config;
            string notice = "";
            try
            {
                config = store.Load(out notice);
            }
            catch (Exception ex)
            {
                Logger.Error("加载配置失败，改用默认配置启动", ex);
                config = new Config();
                notice = "读取配置失败，已使用默认值启动。";
            }

            string autostartNotice = "";
            Autostart.RepairIfNeeded(paths.ExePath, out autostartNotice);
            if (!string.IsNullOrEmpty(autostartNotice))
            {
                notice = string.IsNullOrEmpty(notice) ? autostartNotice : notice + " " + autostartNotice;
            }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += OnUnhandledException;

            int exitCode = 0;
            try
            {
                MainWindow window = new MainWindow(store, config, notice);
                window.Show();
                Logger.Info("主窗口已显示");
                exitCode = app.Run();
            }
            catch (Exception ex)
            {
                Logger.Error("启动失败", ex);
                MessageBox.Show(
                    "EveryGo 启动失败：" + ex.Message + Environment.NewLine + Environment.NewLine
                    + "日志：" + Logger.FilePath,
                    "EveryGo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                exitCode = 1;
            }

            Logger.Info("程序退出，退出码：" + exitCode);
            return exitCode;
        }

        private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("未处理的界面异常", e.Exception);
            MessageBox.Show(
                "EveryGo 遇到错误：" + e.Exception.Message + Environment.NewLine + Environment.NewLine
                + "日志：" + Logger.FilePath,
                "EveryGo",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void ActivateExistingInstance()
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                IntPtr window = NativeMethods.FindWindow(null, "EveryGo");
                if (window != IntPtr.Zero)
                {
                    if (NativeMethods.IsIconic(window))
                    {
                        NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
                    }
                    else
                    {
                        NativeMethods.ShowWindow(window, NativeMethods.SW_SHOW);
                    }
                    NativeMethods.SetForegroundWindow(window);
                    return;
                }
                Thread.Sleep(100);
            }
        }
    }
}
