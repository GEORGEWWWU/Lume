using System;
using System.IO;
using System.Windows;

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
    }
}