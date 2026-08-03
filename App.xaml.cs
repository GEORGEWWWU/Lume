using System;
using System.IO;
using System.Windows;

namespace Lume
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 检查是否有文件通过双击传入
            if (e.Args.Length > 0)
            {
                string openedFilePath = e.Args[0];
                // 【修改这里】：将 .ryen 改回 .lume
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