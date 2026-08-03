using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Lume
{
    public partial class MainWindow : Window
    {
        private string currentFilePath = null;
        private NoteData currentNote;
        private TextBlock currentNoteListTitleUI;
        private bool isDirty = false;
        private string rootWorkspacePath;
        private string currentFolderPath; // 当前选中的文件夹
        private string itemToDeletePath;  // 待删除的文件或文件夹路径
        private bool isDeletingFolder;    // 标记当前删除的是文件夹还是笔记

        public MainWindow()
        {
            InitializeComponent();

            // 1. 初始化工作区目录
            rootWorkspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumeWorkspace");
            if (!Directory.Exists(rootWorkspacePath))
            {
                Directory.CreateDirectory(rootWorkspacePath);
            }

            // 【关键修复1】：如果根目录下没有任何文件夹，自动创建一个“默认文件夹”
            if (Directory.GetDirectories(rootWorkspacePath).Length == 0)
            {
                string defaultFolderPath = Path.Combine(rootWorkspacePath, "默认文件夹");
                Directory.CreateDirectory(defaultFolderPath);
            }

            LoadFolders(); // 渲染左侧文件夹列表

            // 2. 启动时尝试读取上次保存的笔记路径
            string lastOpenedFile = null;
            string configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                string savedPath = File.ReadAllText(configPath);
                if (File.Exists(savedPath))
                {
                    lastOpenedFile = savedPath;
                }
            }

            // 3. 设置环境（加载文件夹和笔记）
            SetupEnvironment(lastOpenedFile);
        }

        public MainWindow(string openedFilePath)
        {
            InitializeComponent();
            SetupEnvironment(openedFilePath);
        }

        private void SetupEnvironment(string openedFilePath)
        {
            currentFilePath = openedFilePath;

            if (!string.IsNullOrEmpty(currentFilePath) && File.Exists(currentFilePath))
            {
                currentFolderPath = Path.GetDirectoryName(currentFilePath);
                LoadFolders(); // 刷新左侧列表，让选中的文件夹高亮
                LoadNotes(currentFolderPath);

                LoadNote();
                ShowEditor(true);
            }
            else
            {
                string[] folders = Directory.GetDirectories(rootWorkspacePath);
                if (folders.Length > 0)
                {
                    currentFolderPath = folders[0];
                    LoadFolders(); // 刷新左侧列表，让第一个文件夹高亮
                    LoadNotes(currentFolderPath);
                }

                ShowEditor(false);
            }
        }

        private void ShowEditor(bool isVisible)
        {
            if (isVisible)
            {
                EmptyStateText.Visibility = Visibility.Collapsed;
                EditorContainer.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyStateText.Visibility = Visibility.Visible;
                EditorContainer.Visibility = Visibility.Collapsed;
            }
        }

        private void EditorArea_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (EditorContainer.Visibility == Visibility.Visible) return;

            currentNote = new NoteData();
            currentFilePath = null;
            isDirty = true; // 新建文件天然算作修改过

            NoteTitleBox.Text = currentNote.Title;
            NoteEditor.Document.Blocks.Clear();
            NoteEditor.Document.Blocks.Add(new Paragraph());
            StatusText.Text = "新笔记 (未保存)";

            ShowEditor(true);

            NoteTitleBox.Focus();
            NoteTitleBox.CaretIndex = NoteTitleBox.Text.Length;
        }

        private void BtnAddNote_Click(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(currentFolderPath))
            {
                MessageBox.Show("请先在左侧选择或创建一个文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string baseName = "新笔记";
            string newFilePath = Path.Combine(currentFolderPath, baseName + ".lume");
            int counter = 1;

            while (File.Exists(newFilePath))
            {
                newFilePath = Path.Combine(currentFolderPath, $"{baseName} ({counter}).lume");
                counter++;
            }

            // 创建空文件并保存
            NoteData newNote = new NoteData { Title = "新笔记", DateCreated = DateTime.Now.ToString("yyyy/MM/dd") };
            LumeFileManager.SaveLumeFile(newFilePath, newNote);

            LoadNotes(currentFolderPath); // 刷新笔记列表

            // 将当前文件路径设为刚新建的文件，打开编辑器，并把光标焦点放到标题上
            currentFilePath = newFilePath;
            LoadNote();
            ShowEditor(true);

            NoteTitleBox.Focus();       // 聚焦到标题
            NoteTitleBox.SelectAll();   // 全选"新笔记"三个字，方便用户直接打字替换
        }

        private void LoadNotes(string folderPath)
        {
            NoteListPanel.Children.Clear();
            if (!Directory.Exists(folderPath)) return;

            string[] noteFiles = Directory.GetFiles(folderPath, "*.lume");
            foreach (string file in noteFiles)
            {
                NoteData noteData = LumeFileManager.OpenLumeFile(file);

                Border cardBorder = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 243, 243)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 0, 0, 10),
                    Cursor = Cursors.Hand
                };

                // 挂载右键删除菜单
                ContextMenu ctx = new ContextMenu();
                MenuItem deleteItem = new MenuItem { Header = "删除笔记" };
                deleteItem.Click += (s, e) => ShowDeleteDialog(file, false);
                ctx.Items.Add(deleteItem);
                cardBorder.ContextMenu = ctx;

                // 左键点击打开笔记到编辑器
                cardBorder.MouseLeftButtonDown += (s, e) =>
                {
                    currentFilePath = file;
                    LoadNote();
                    ShowEditor(true);
                };

                StackPanel cardStack = new StackPanel();
                TextBlock titleText = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(noteData.Title) ? "无标题笔记" : noteData.Title,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                TextBlock dateText = new TextBlock
                {
                    Text = noteData.DateCreated,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(136, 136, 136)),
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                cardStack.Children.Add(titleText);
                cardStack.Children.Add(dateText);
                cardBorder.Child = cardStack;
                NoteListPanel.Children.Add(cardBorder);
            }
        }

        private void LoadNote()
        {
            currentNote = LumeFileManager.OpenLumeFile(currentFilePath);
            NoteTitleBox.Text = currentNote.Title;

            if (!string.IsNullOrEmpty(currentNote.ContentRtf))
            {
                using (MemoryStream ms = new MemoryStream(System.Text.Encoding.Default.GetBytes(currentNote.ContentRtf)))
                {
                    TextRange textRange = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd);
                    textRange.Load(ms, DataFormats.Rtf);
                }
            }
            isDirty = false; // 加载后状态是干净的
        }

        // 监听全局鼠标点击，判断是否点到了外部
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 只要编辑器可见，点击外侧就应该清除焦点（取消原本外层对 && isDirty 的强依赖）
            if (EditorContainer.Visibility == Visibility.Visible)
            {
                // 如果点到了红绿灯按钮，让关闭按钮自己的逻辑去处理
                if (e.OriginalSource is System.Windows.Shapes.Ellipse) return;

                // 获取鼠标相对右侧区域的坐标
                Point pos = e.GetPosition(RightEditArea);

                // 只要鼠标 X 或 Y 超出了右侧区域的边界
                if (pos.X < 0 || pos.Y < 0 || pos.X > RightEditArea.ActualWidth || pos.Y > RightEditArea.ActualHeight)
                {
                    // 只有当有修改时，才执行保存
                    if (isDirty)
                    {
                        SaveNote();
                    }

                    // 强行清除文本框的键盘焦点，并将焦点归还给主窗口，光标彻底消失
                    Keyboard.ClearFocus();
                    this.Focus();
                }
            }
        }

        private string GetConfigPath()
        {
            // 将最后一次打开的文件路径保存在系统的 AppData 目录下
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lume");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "last_note.txt");
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true; // 只要敲了键盘，就标记为已修改
            if (StatusText != null) StatusText.Text = "编辑中 (点击外部空白处即可保存)...";

            if (currentNoteListTitleUI != null && NoteTitleBox != null)
            {
                currentNoteListTitleUI.Text = string.IsNullOrWhiteSpace(NoteTitleBox.Text) ? "无标题笔记" : NoteTitleBox.Text;
            }
        }

        private bool SaveNote()
        {
            if (!isDirty) return true; // 没修改就不保存
            if (EditorContainer.Visibility != Visibility.Visible) return true;

            // 【解答第一个问题】：保存时如果标题被清空了，强行把右侧文本框填上“无标题笔记”
            if (string.IsNullOrWhiteSpace(NoteTitleBox.Text))
            {
                NoteTitleBox.Text = "无标题笔记";
            }

            if (currentNote == null) currentNote = new NoteData();
            currentNote.Title = NoteTitleBox.Text;

            TextRange textRange = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd);
            using (MemoryStream ms = new MemoryStream())
            {
                textRange.Save(ms, DataFormats.Rtf);
                currentNote.ContentRtf = System.Text.Encoding.Default.GetString(ms.ToArray());
            }

            if (string.IsNullOrEmpty(currentFilePath))
            {
                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.Title = "选择笔记保存位置";
                dialog.Filter = "Lume 笔记 (*.lume)|*.lume";
                dialog.DefaultExt = ".lume";

                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                dialog.FileName = GenerateDefaultFileName(dialog.InitialDirectory);

                if (dialog.ShowDialog() == true)
                {
                    currentFilePath = dialog.FileName;
                }
                else
                {
                    StatusText.Text = "未保存 (草稿)";
                    return false;
                }
            }

            LumeFileManager.SaveLumeFile(currentFilePath, currentNote);
            StatusText.Text = $"已保存到本地 {DateTime.Now:HH:mm:ss}";

            // 保存成功后，记录当前文件路径，以便下次启动时自动加载
            File.WriteAllText(GetConfigPath(), currentFilePath);

            isDirty = false; // 保存成功，恢复干净状态
            return true;
        }

        private string GenerateDefaultFileName(string directoryPath)
        {
            string dateStr = DateTime.Now.ToString("yyMMdd");
            int seq = 1;

            while (true)
            {
                string seqStr = seq.ToString("D2");
                string testName = $"Lume{dateStr}{seqStr}.lume";
                string fullPath = Path.Combine(directoryPath, testName);

                if (!File.Exists(fullPath))
                {
                    return $"Lume{dateStr}{seqStr}";
                }
                seq++;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, MouseButtonEventArgs e)
        {
            bool isSaved = SaveNote();
            if (isSaved)
            {
                Application.Current.Shutdown();
            }
        }

        private void BtnMinimize_Click(object sender, MouseButtonEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, MouseButtonEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void BtnAddFolder_Click(object sender, MouseButtonEventArgs e)
        {
            string baseName = "未命名文件夹";
            string newFolderPath = Path.Combine(rootWorkspacePath, baseName);
            int counter = 1;

            // 自动重命名以避免冲突
            while (Directory.Exists(newFolderPath))
            {
                newFolderPath = Path.Combine(rootWorkspacePath, $"{baseName} ({counter})");
                counter++;
            }

            Directory.CreateDirectory(newFolderPath);
            LoadFolders(); // 刷新文件夹列表
        }

        private void LoadFolders()
        {
            if (FolderListPanel == null) return;
            FolderListPanel.Children.Clear();

            string[] folders = Directory.GetDirectories(rootWorkspacePath);
            foreach (string folder in folders)
            {
                // 判断当前循环到的文件夹，是不是我们选中的文件夹
                bool isSelected = (folder == currentFolderPath);

                Border folderBorder = new Border
                {
                    // 如果选中了，就给一个浅灰色背景，否则透明
                    Background = new System.Windows.Media.SolidColorBrush(
                        isSelected ? System.Windows.Media.Color.FromRgb(225, 225, 225) : System.Windows.Media.Colors.Transparent),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(10, 2, 10, 2),
                    Cursor = Cursors.Hand
                };

                ContextMenu ctx = new ContextMenu();
                MenuItem deleteItem = new MenuItem { Header = "删除文件夹" };
                deleteItem.Click += (s, e) => ShowDeleteDialog(folder, true);
                ctx.Items.Add(deleteItem);
                folderBorder.ContextMenu = ctx;

                folderBorder.MouseLeftButtonDown += (s, e) =>
                {
                    currentFolderPath = folder;
                    LoadFolders(); // 点击后，重新刷新一次所有文件夹的颜色
                    LoadNotes(folder);
                };

                TextBlock text = new TextBlock
                {
                    Text = "📁 " + Path.GetFileName(folder),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51))
                };

                folderBorder.Child = text;
                FolderListPanel.Children.Add(folderBorder);
            }
        }

        private void ShowDeleteDialog(string path, bool isFolder)
        {
            itemToDeletePath = path;
            isDeletingFolder = isFolder;
            DeleteConfirmDialog.Visibility = Visibility.Visible;
        }

        private void BtnCancelDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmDialog.Visibility = Visibility.Collapsed;
            itemToDeletePath = null;
        }

        private void BtnConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (isDeletingFolder && Directory.Exists(itemToDeletePath))
                {
                    Directory.Delete(itemToDeletePath, true); // true表示连同内部文件一起删除
                    if (currentFolderPath == itemToDeletePath)
                    {
                        currentFolderPath = null;
                        NoteListPanel.Children.Clear();
                        ShowEditor(false);
                    }
                    LoadFolders();
                }
                else if (!isDeletingFolder && File.Exists(itemToDeletePath))
                {
                    File.Delete(itemToDeletePath);
                    if (currentFilePath == itemToDeletePath)
                    {
                        currentFilePath = null;
                        ShowEditor(false);
                    }
                    if (!string.IsNullOrEmpty(currentFolderPath))
                    {
                        LoadNotes(currentFolderPath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}");
            }
            finally
            {
                DeleteConfirmDialog.Visibility = Visibility.Collapsed;
                itemToDeletePath = null;
            }
        }
    }
}