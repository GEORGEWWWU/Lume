using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Diagnostics;

namespace Lume
{
    public partial class App : Application
    {
        public App()
        {
            // 1. 捕获 UI 线程的异常（比如 XAML 找不到图片、控件初始化失败等）
            this.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show($"致命错误导致启动失败：\n{e.Exception.Message}\n\n内部原因：{e.Exception.InnerException?.Message}",
                                "UI崩溃提示", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true; // 阻止程序直接闪退
            };

            // 2. 捕获非 UI 线程的异常（比如后台读写文件报错）
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    MessageBox.Show($"严重错误：\n{ex.Message}", "后台崩溃提示", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 启动时自动注册文件关联
            RegisterFileAssociation();

            // 检查是否有文件通过双击传入
            if (e.Args.Length > 0)
            {
                string openedFilePath = e.Args[0];
                if (Path.GetExtension(openedFilePath).Equals(".lume", StringComparison.OrdinalIgnoreCase))
                {
                    MainWindow mainWindow = new MainWindow(openedFilePath);
                    mainWindow.Show();
                    return;
                }
            }

            // 普通启动
            new MainWindow().Show();
        }

        private void RegisterFileAssociation()
        {
            try
            {
                string extension = ".lume";
                string progId = "Lume.NoteFile";
                // 获取当前运行的 Lume.exe 的完整绝对路径
                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                // 1. 在当前用户注册表中，将 .lume 扩展名指向我们的 ProgID
                using (RegistryKey extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
                {
                    extKey.SetValue("", progId);
                }

                // 2. 设置 ProgID 的具体信息（显示名称、图标、打开命令）
                using (RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
                {
                    progIdKey.SetValue("", "Lume 笔记文件"); // 文件类型描述

                    // 设置默认图标 (直接使用你的 exe 自带图标)
                    using (RegistryKey iconKey = progIdKey.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue("", $"\"{exePath}\",0");
                    }

                    // 设置双击时的打开命令，%1 代表传进来的文件路径
                    using (RegistryKey cmdKey = progIdKey.CreateSubKey(@"shell\open\command"))
                    {
                        cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }
            }
            catch (Exception)
            {
                // 写入注册表可能会因为权限问题失败，普通用户环境一般没问题，这里静默吞掉异常即可
            }
        }
    }
}