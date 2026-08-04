using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Lume
{
    public partial class App : Application
    {
        // 声明一个静态互斥锁，确保全局唯一
        private static Mutex appMutex;
        // 命名管道的名称，必须唯一
        private const string PipeName = "Lume_SingleInstance_Pipe";

        public App()
        {
            // 1. 捕获 UI 线程的异常
            this.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show($"致命错误导致启动失败：\n{e.Exception.Message}\n\n内部原因：{e.Exception.InnerException?.Message}",
                                "UI崩溃提示", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            // 2. 捕获非 UI 线程的异常
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
            // 尝试获取互斥锁
            appMutex = new Mutex(true, "LumeApp_SingleInstance_Mutex", out bool createdNew);

            if (!createdNew)
            {
                // 如果 createdNew 是 false，说明已经有一个 Lume 正在运行了！
                if (e.Args.Length > 0)
                {
                    // 把双击传入的文件路径，发送给正在运行的老进程
                    SendArgsToExistingInstance(e.Args[0]);
                }

                // 新进程立刻退出，保证只有一个进程！
                Current.Shutdown();
                return;
            }

            // ================= 以下是正常启动（第一个进程）的逻辑 =================
            base.OnStartup(e);

            RegisterFileAssociation();

            MainWindow mainWindow;
            if (e.Args.Length > 0 && Path.GetExtension(e.Args[0]).Equals(".lume", StringComparison.OrdinalIgnoreCase))
            {
                mainWindow = new MainWindow(e.Args[0]);
            }
            else
            {
                mainWindow = new MainWindow();
            }

            mainWindow.Show();

            // 开启后台线程，监听有没有其他的“影子进程”把文件路径丢过来
            Task.Run(() => StartPipeServer(mainWindow));
        }

        // 把参数通过命名管道发送给正在运行的实例
        private void SendArgsToExistingInstance(string filePath)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(1000); // 超时时间 1 秒
                    byte[] bytes = Encoding.UTF8.GetBytes(filePath);
                    client.Write(bytes, 0, bytes.Length);
                }
            }
            catch { /* 如果发送失败直接吞掉，不影响退出 */ }
        }

        // 监听其他进程发来的消息
        private void StartPipeServer(MainWindow mainWindow)
        {
            while (true)
            {
                try
                {
                    using (NamedPipeServerStream server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                    {
                        server.WaitForConnection(); // 阻塞等待新进程连接
                        using (StreamReader reader = new StreamReader(server, Encoding.UTF8))
                        {
                            string filePath = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(filePath))
                            {
                                // 切回 UI 线程去操作 MainWindow
                                Dispatcher.Invoke(() =>
                                {
                                    // 如果窗口最小化了，把它弹出来
                                    if (mainWindow.WindowState == WindowState.Minimized)
                                        mainWindow.WindowState = WindowState.Normal;

                                    mainWindow.Activate(); // 让主窗口获取焦点

                                    // 调用 MainWindow 的方法加载新笔记
                                    mainWindow.OpenExternalFile(filePath);
                                });
                            }
                        }
                    }
                }
                catch { /* 忽略通信异常，继续下一次监听 */ }
            }
        }

        private void RegisterFileAssociation()
        {
            // ... 保持你原来的 RegisterFileAssociation 逻辑不变 ...
            try
            {
                string extension = ".lume";
                string progId = "Lume.NoteFile";
                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                using (RegistryKey extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}"))
                {
                    extKey.SetValue("", progId);
                    using (RegistryKey shellNewKey = extKey.CreateSubKey("ShellNew"))
                    {
                        shellNewKey.SetValue("NullFile", "");
                    }
                }

                using (RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}"))
                {
                    progIdKey.SetValue("", "Lume 笔记文件");
                    using (RegistryKey iconKey = progIdKey.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue("", $"\"{exePath}\",0");
                    }
                    using (RegistryKey cmdKey = progIdKey.CreateSubKey(@"shell\open\command"))
                    {
                        cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }
            }
            catch (Exception) { }
        }
    }
}