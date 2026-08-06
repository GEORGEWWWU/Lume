using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace Lume
{
    public partial class MainWindow : Window
    {
        private string currentFilePath = null;
        private NoteData currentNote;
        private TextBlock currentNoteListTitleUI;
        private bool isDirty = false;
        private bool isLoadingNote = false; // 新增：标记是否正在用代码加载笔记
        private string rootWorkspacePath;
        private string currentFolderPath; // 当前选中的文件夹
        private string itemToDeletePath;  // 待删除的文件或文件夹路径
        private bool isDeletingFolder;    // 标记当前删除的是文件夹还是笔记
        private bool isSidebarOpen = true;
        private const string VIRTUAL_EXTERNAL_FOLDER = "VIRTUAL_EXTERNAL"; // 虚拟文件夹标识
        private System.Windows.Threading.DispatcherTimer searchTimer;
        private bool isSidebarAnimating = false; // 侧边栏动画锁
        private double _currentZoomFactor = 1.0;
        private const double BaseTitleFontSize = 28.0;  // 标题基准字号
        private const double BaseEditorFontSize = 15.0; // 正文基准字号

        public MainWindow()
        {
            InitializeComponent();
            SetupVersionBadge();

            NoteEditor.Options.InheritWordWrapIndentation = false;

            // 一键启用内置的 Markdown 语法高亮
            NoteEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("MarkDown");

            // 监听文本光标的移动
            NoteEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;

            // 注册彩色 Emoji 渲染器
            NoteEditor.TextArea.TextView.ElementGenerators.Add(new EmojiElementGenerator());

            // 修改文本框选的背景颜色和文本颜色
            // 设置背景色为淡灰色
            NoteEditor.TextArea.SelectionBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8"));
            // 移除默认的蓝色细边框
            NoteEditor.TextArea.SelectionBorder = null;
            // 强制选中时的文本颜色为深色（黑色或你原本的 #333333），防止字变白看不清！
            NoteEditor.TextArea.SelectionForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);

            // 1. 初始化工作区目录
            rootWorkspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumeWorkspace");
            if (!Directory.Exists(rootWorkspacePath))
            {
                Directory.CreateDirectory(rootWorkspacePath);
            }

            if (Directory.GetDirectories(rootWorkspacePath).Length == 0)
            {
                string defaultFolderPath = Path.Combine(rootWorkspacePath, "默认文件夹");
                Directory.CreateDirectory(defaultFolderPath);
            }

            LoadFolders(); // 渲染左侧文件夹列表

            // 2. 【修改这里】：尝试读取上次保存的文件夹路径
            string configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                string savedPath = File.ReadAllText(configPath);

                // 兼容老版本（如果旧配置里存的是笔记文件，就提取它的文件夹）
                if (File.Exists(savedPath))
                {
                    currentFolderPath = Path.GetDirectoryName(savedPath);
                }
                // 正常情况（读取的是之前关闭时保存的文件夹）
                else if (Directory.Exists(savedPath))
                {
                    currentFolderPath = savedPath;
                }
            }

            // 3. 强制传入 null，确保启动时绝对不会选中和打开任何笔记
            SetupEnvironment(null);

            searchTimer = new System.Windows.Threading.DispatcherTimer();
            searchTimer.Interval = TimeSpan.FromMilliseconds(400); // 停止打字 400 毫秒后才执行搜索
            searchTimer.Tick += (s, args) =>
            {
                searchTimer.Stop();
                if (!string.IsNullOrEmpty(currentFolderPath))
                {
                    LoadNotes(currentFolderPath);
                }
            };
        }

        public MainWindow(string openedFilePath)
        {
            InitializeComponent();
            SetupVersionBadge();

            // 注册彩色 Emoji 渲染器
            NoteEditor.TextArea.TextView.ElementGenerators.Add(new EmojiElementGenerator());

            // 一键启用内置的 Markdown 语法高亮
            NoteEditor.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("MarkDown");

            // 修改文本框选的背景颜色和文本颜色
            // 设置背景色为淡灰色
            NoteEditor.TextArea.SelectionBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8"));
            // 移除默认的蓝色细边框
            NoteEditor.TextArea.SelectionBorder = null;
            // 强制选中时的文本颜色为深色（黑色或你原本的 #333333），防止字变白看不清！
            NoteEditor.TextArea.SelectionForeground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);

            // 补上工作区路径初始化（防止 rootWorkspacePath 为 null）
            rootWorkspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumeWorkspace");
            if (!Directory.Exists(rootWorkspacePath))
            {
                Directory.CreateDirectory(rootWorkspacePath);
            }

            SetupEnvironment(openedFilePath);
        }

        // 添加这个公开方法，专门用来接收其他进程发来的外部文件
        public void OpenExternalFile(string filePath)
        {
            // 如果当前有没保存的内容，先触发保存逻辑
            if (isDirty)
            {
                SaveNote();
            }

            // 直接复用你现成的环境初始化代码！
            SetupEnvironment(filePath);
        }

        private void SetupEnvironment(string openedFilePath)
        {
            currentFilePath = openedFilePath;

            if (string.IsNullOrEmpty(rootWorkspacePath))
            {
                rootWorkspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumeWorkspace");
                if (!Directory.Exists(rootWorkspacePath))
                {
                    Directory.CreateDirectory(rootWorkspacePath);
                }
            }

            string[] folders = Directory.GetDirectories(rootWorkspacePath);
            if (folders.Length == 0)
            {
                Directory.CreateDirectory(Path.Combine(rootWorkspacePath, "默认文件夹"));
                folders = Directory.GetDirectories(rootWorkspacePath);
            }

            if (!string.IsNullOrEmpty(currentFilePath) && File.Exists(currentFilePath))
            {
                string noteFolder = Path.GetDirectoryName(currentFilePath);

                // 判断文件是否在 Lume 工作区内
                if (noteFolder.StartsWith(rootWorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    currentFolderPath = noteFolder;
                }
                else
                {
                    // 外部打开的文件：原位不动！仅添加到外部历史记录，并切换到虚拟分类
                    AddExternalNotePath(currentFilePath);
                    currentFolderPath = VIRTUAL_EXTERNAL_FOLDER;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(currentFolderPath) ||
                    (!Directory.Exists(currentFolderPath) && currentFolderPath != VIRTUAL_EXTERNAL_FOLDER) ||
                    (!currentFolderPath.StartsWith(rootWorkspacePath, StringComparison.OrdinalIgnoreCase) && currentFolderPath != VIRTUAL_EXTERNAL_FOLDER))
                {
                    currentFolderPath = folders[0];
                }
            }

            LoadFolders();
            LoadNotes(currentFolderPath);

            if (!string.IsNullOrEmpty(currentFilePath) && File.Exists(currentFilePath))
            {
                LoadNote();
                ShowEditor(true);
            }
            else
            {
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

            // 【关键修复】：不要制造 currentFilePath = null 的孤儿笔记！
            // 强制调用常规的“新建笔记”逻辑，确保笔记归属于当前高亮的文件夹
            BtnAddNote_Click(null, null);
        }

        private void BtnAddNote_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox != null) SearchTextBox.Text = "";

            // 拦截虚拟外部笔记分类中的新建操作
            if (currentFolderPath == VIRTUAL_EXTERNAL_FOLDER)
            {
                MessageBox.Show("「外部笔记」仅用于查看历史记录，无法在此处直接新建笔记！\n请在左侧选择一个普通文件夹。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(currentFolderPath))
            {
                MessageBox.Show("请先在左侧选择或创建一个文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string dateStr = DateTime.Now.ToString("yyMMdd");
            int seq = 1;
            string newFilePath;

            while (true)
            {
                string seqStr = seq.ToString("D2");
                newFilePath = Path.Combine(currentFolderPath, $"lume{dateStr}{seqStr}.lume");

                if (!File.Exists(newFilePath)) break;
                seq++;
            }

            NoteData newNote = new NoteData { Title = "新笔记", DateCreated = DateTime.Now.ToString("yyyy/MM/dd") };
            LumeFileManager.SaveLumeFile(newFilePath, newNote);

            currentFilePath = newFilePath;
            LoadNote();
            ShowEditor(true);

            LoadNotes(currentFolderPath);

            NoteTitleBox.Focus();
            NoteTitleBox.SelectAll();
        }

        private void LoadNotes(string folderPath)
        {
            NoteListPanel.Children.Clear();
            if (string.IsNullOrEmpty(rootWorkspacePath)) return;

            // 动态控制：处于外部笔记分类时隐藏新建按钮
            bool isVirtualFolder = (folderPath == VIRTUAL_EXTERNAL_FOLDER);
            if (BtnAddNoteText != null) BtnAddNoteText.Visibility = isVirtualFolder ? Visibility.Collapsed : Visibility.Visible;
            if (BtnTopAddNote != null) BtnTopAddNote.Visibility = isVirtualFolder ? Visibility.Collapsed : Visibility.Visible;

            string keyword = SearchTextBox?.Text?.Trim();
            bool isSearchMode = !string.IsNullOrEmpty(keyword);

            IEnumerable<string> noteFiles;

            if (TopFolderNameText != null) TopFolderNameText.Text = "All Lume";

            if (isSearchMode)
            {
                noteFiles = Directory.GetFiles(rootWorkspacePath, "*.lume", SearchOption.AllDirectories)
                                     .Concat(GetExternalNotePaths())
                                     .Where(f => File.Exists(f))
                                     .Distinct();
            }
            else if (folderPath == VIRTUAL_EXTERNAL_FOLDER)
            {
                noteFiles = GetExternalNotePaths().Where(f => File.Exists(f));
            }
            else
            {
                if (!Directory.Exists(folderPath)) return;
                noteFiles = Directory.GetFiles(folderPath, "*.lume")
                                     .Select(f => new FileInfo(f))
                                     .OrderByDescending(f => f.CreationTime)
                                     .Select(f => f.FullName);
            }

            int noteCount = 0;

            foreach (string file in noteFiles)
            {
                NoteData noteData = LumeFileManager.OpenLumeFile(file);

                if (isSearchMode)
                {
                    bool matchTitle = !string.IsNullOrEmpty(noteData.Title) && noteData.Title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                    // 优先搜新格式，如果为空再去旧 RTF 里捞纯文本
                    string plainText = !string.IsNullOrEmpty(noteData.ContentText) ? noteData.ContentText : ExtractTextFromRtf(noteData.ContentRtf);
                    bool matchContent = !string.IsNullOrEmpty(plainText) && plainText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!matchTitle && !matchContent) continue;
                }

                noteCount++;
                bool isSelected = (file == currentFilePath);

                Border cardBorder = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        isSelected ? System.Windows.Media.Color.FromRgb(243, 243, 243) : System.Windows.Media.Colors.Transparent),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 0, 0, 10),
                    Cursor = Cursors.Hand
                };

                ContextMenu ctx = new ContextMenu();
                if (folderPath == VIRTUAL_EXTERNAL_FOLDER)
                {
                    MenuItem removeRecordItem = new MenuItem { Header = "从列表中移除记录" };
                    removeRecordItem.Click += (s, e) =>
                    {
                        RemoveExternalNotePath(file);
                        if (currentFilePath == file)
                        {
                            currentFilePath = null;
                            ShowEditor(false);
                        }
                        LoadNotes(folderPath);
                    };
                    ctx.Items.Add(removeRecordItem);
                }
                else
                {
                    MenuItem deleteItem = new MenuItem { Header = "删除笔记" };
                    deleteItem.Click += (s, e) => ShowDeleteDialog(file, false);
                    ctx.Items.Add(deleteItem);
                }
                cardBorder.ContextMenu = ctx;

                cardBorder.MouseLeftButtonDown += (s, e) =>
                {
                    // 1. 如果点击的是当前正在编辑的笔记，直接返回，避免重复加载
                    if (currentFilePath == file) return;

                    // 2. 如果当前有未保存的修改，先保存
                    if (isDirty) SaveNote();

                    currentFilePath = file;
                    LoadNote();
                    ShowEditor(true);

                    // 3. 告别重载，手动更新 UI 选中状态（极其丝滑）
                    foreach (var child in NoteListPanel.Children)
                    {
                        if (child is Border b)
                        {
                            b.Background = System.Windows.Media.Brushes.Transparent;
                        }
                    }
                    cardBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 243, 243));
                };

                StackPanel cardStack = new StackPanel();
                DateTime lastWriteTime = File.GetLastWriteTime(file);

                TextBlock dateText = new TextBlock
                {
                    Text = lastWriteTime.ToString("yyyy年M月d日 HH:mm"),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 153, 153)),
                    FontSize = 12,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                TextBlock titleText = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(noteData.Title) ? "无标题笔记" : noteData.Title,
                    FontWeight = FontWeights.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                cardStack.Children.Add(titleText);
                cardStack.Children.Add(dateText);
                cardBorder.Child = cardStack;
                NoteListPanel.Children.Add(cardBorder);
            }

            if (TopNoteCountText != null)
            {
                if (isSearchMode)
                {
                    // 搜索模式下，显示搜索出的结果数量
                    TopNoteCountText.Text = $"{noteCount} 个结果";
                }
                else
                {
                    // 正常模式下，强制计算并显示“全局所有笔记”的总数
                    int globalNoteCount = Directory.GetFiles(rootWorkspacePath, "*.lume", SearchOption.AllDirectories)
                                         .Concat(GetExternalNotePaths())
                                         .Where(f => File.Exists(f))
                                         .Distinct()
                                         .Count();

                    TopNoteCountText.Text = $"{globalNoteCount} notes";
                }
            }
        }

        private void LoadNote()
        {
            isLoadingNote = true;

            currentNote = LumeFileManager.OpenLumeFile(currentFilePath);
            NoteTitleBox.Text = currentNote.Title;

            // --- AvalonEdit 高性能加载逻辑 ---
            if (!string.IsNullOrEmpty(currentNote.ContentText))
            {
                NoteEditor.Text = currentNote.ContentText;
            }
            else if (!string.IsNullOrEmpty(currentNote.ContentRtf))
            {
                NoteEditor.Text = ExtractTextFromRtf(currentNote.ContentRtf);
            }
            else
            {
                NoteEditor.Text = "";
            }

            // 恢复当前笔记特有的缩放比例（若为 0 或未设置则兜底为 1.0）
            _currentZoomFactor = (currentNote.ZoomFactor <= 0) ? 1.0 : currentNote.ZoomFactor;
            NoteTitleBox.FontSize = BaseTitleFontSize * _currentZoomFactor;
            NoteEditor.FontSize = BaseEditorFontSize * _currentZoomFactor;

            // 笔记加载完成后，立刻初始化底部数据
            if (CharCountText != null) CharCountText.Text = $"{NoteEditor.Text.Length} 个字符";
            if (ZoomText != null) ZoomText.Text = $"{Math.Round(_currentZoomFactor * 100)}%";
            // 强制光标归位提示
            if (CursorPositionText != null) CursorPositionText.Text = "第 1 行，第 1 列";

            isDirty = false;

            DateTime lastWriteTime = File.GetLastWriteTime(currentFilePath);
            StatusText.Text = $"最后编辑于 {lastWriteTime:yyyy/MM/dd HH:mm}";

            if (!string.IsNullOrEmpty(currentFilePath))
            {
                if (currentFolderPath == VIRTUAL_EXTERNAL_FOLDER)
                {
                    FolderPathText.Text = $"外部文件：{currentFilePath}";
                }
                else
                {
                    string parentFolderName = Path.GetFileName(Path.GetDirectoryName(currentFilePath));
                    FolderPathText.Text = $"归档于 {parentFolderName}";
                }
            }

            isLoadingNote = false;
        }

        // 监听全局鼠标点击，判断是否点到了外部
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 处理搜索框的外部点击失焦
            if (SearchTextBox != null && SearchTextBox.IsFocused)
            {
                // 获取鼠标相对于 SearchRing (外圈边框) 的坐标
                Point pos = e.GetPosition(SearchRing);

                // 如果鼠标点击在搜索框外面，立刻清理焦点
                if (pos.X < 0 || pos.Y < 0 || pos.X > SearchRing.ActualWidth || pos.Y > SearchRing.ActualHeight)
                {
                    Keyboard.ClearFocus();
                    this.Focus();
                }
            }

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

        // 监听全局键盘事件，拦截 Ctrl + S
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 判断是否按下了 Ctrl 键和 S 键
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // 只有当编辑器处于显示状态，且笔记被修改过才进行保存
                if (EditorContainer.Visibility == Visibility.Visible && isDirty)
                {
                    SaveNote();

                    // 【可选体验优化】：如果你希望 Ctrl+S 后和点击外部一样失去焦点，可以取消下面两行的注释
                    // Keyboard.ClearFocus();
                    // this.Focus();
                }

                // 标记事件已处理，防止其他控件（如 RichTextBox）继续响应此组合键
                e.Handled = true;
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
            if (isLoadingNote) return; // 如果是代码在自动填充内容，直接退出，不执行下面的逻辑

            isDirty = true;
            if (StatusText != null) StatusText.Text = "编辑中 (点击外部空白处即可保存)...";

            if (currentNoteListTitleUI != null && NoteTitleBox != null)
            {
                currentNoteListTitleUI.Text = string.IsNullOrWhiteSpace(NoteTitleBox.Text) ? "无标题笔记" : NoteTitleBox.Text;
            }
        }

        private void NoteEditor_TextChanged(object sender, EventArgs e)
        {
            if (isLoadingNote) return;
            isDirty = true;
            if (StatusText != null) StatusText.Text = "编辑中 (点击外部空白处即可保存)...";

            // 实时更新字符总数
            if (CharCountText != null) CharCountText.Text = $"{NoteEditor.Text.Length} 个字符";
        }

        private bool SaveNote()
        {
            if (!isDirty) return true; // 没修改就不保存
            if (EditorContainer.Visibility != Visibility.Visible) return true;

            if (string.IsNullOrWhiteSpace(NoteTitleBox.Text))
            {
                NoteTitleBox.Text = "无标题笔记";
            }

            if (currentNote == null) currentNote = new NoteData();
            currentNote.Title = NoteTitleBox.Text;

            // --- AvalonEdit 极速保存逻辑 ---
            // 直接获取文本，抛弃沉重的 RTF 内存流
            currentNote.ContentText = NoteEditor.Text;
            currentNote.ContentRtf = ""; // 清空旧的富文本数据，大幅度减小存储体积

            // 写入当前笔记的缩放比例
            currentNote.ZoomFactor = _currentZoomFactor;

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

            isDirty = false;

            if (currentNoteListTitleUI != null && currentNote != null)
            {
                currentNoteListTitleUI.Text = string.IsNullOrWhiteSpace(currentNote.Title) ? "无标题笔记" : currentNote.Title;
            }

            return true;
        }

        private string GenerateDefaultFileName(string directoryPath)
        {
            string dateStr = DateTime.Now.ToString("yyMMdd");
            int seq = 1;

            while (true)
            {
                string seqStr = seq.ToString("D2");
                // 把这里的大写 Lume 统一改成小写 lume
                string testName = $"lume{dateStr}{seqStr}.lume";
                string fullPath = Path.Combine(directoryPath, testName);

                if (!File.Exists(fullPath))
                {
                    return $"lume{dateStr}{seqStr}";
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
                if (!string.IsNullOrEmpty(currentFolderPath))
                {
                    File.WriteAllText(GetConfigPath(), currentFolderPath);
                }

                // 调用系统内置关闭命令，触发系统淡出/缩放动画
                SystemCommands.CloseWindow(this);
            }
        }

        private void BtnMinimize_Click(object sender, MouseButtonEventArgs e)
        {
            // 调用系统内置最小化命令，触发平滑缩放到任务栏动画
            SystemCommands.MinimizeWindow(this);
        }

        private void BtnMaximize_Click(object sender, MouseButtonEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
        }

        private void BtnAddFolder_Click(object sender, MouseButtonEventArgs e)
        {
            string baseName = "未命名文件夹";
            string newFolderPath = Path.Combine(rootWorkspacePath, baseName);
            int counter = 1;

            while (Directory.Exists(newFolderPath))
            {
                newFolderPath = Path.Combine(rootWorkspacePath, $"{baseName} ({counter})");
                counter++;
            }

            Directory.CreateDirectory(newFolderPath);

            // 【关键修复】：新建了文件夹，必须立刻让它变成“当前正在使用的文件夹”
            currentFolderPath = newFolderPath;

            // 如果之前有正在编辑的笔记，现在切到新文件夹了，右侧必须清空关闭
            if (isDirty) SaveNote();
            currentFilePath = null;
            ShowEditor(false);

            LoadFolders();
            LoadNotes(currentFolderPath);
        }

        private void LoadFolders()
        {
            if (FolderListPanel == null) return;
            FolderListPanel.Children.Clear();

            // 1. 获取所有物理文件夹
            var allFolders = Directory.GetDirectories(rootWorkspacePath);

            // 提取“默认文件夹”和其他普通文件夹（普通文件夹按创建时间倒序）
            string defaultFolder = allFolders.FirstOrDefault(f => Path.GetFileName(f) == "默认文件夹");
            var otherFolders = allFolders.Where(f => Path.GetFileName(f) != "默认文件夹")
                                         .OrderByDescending(f => Directory.GetCreationTime(f));

            // ==================== 顺序 1：渲染默认文件夹 ====================
            if (defaultFolder != null)
            {
                RenderFolderItem(defaultFolder);
            }

            // ==================== 顺序 2：渲染“外部笔记”（紧跟默认文件夹） ====================
            bool isExternalSelected = currentFolderPath == VIRTUAL_EXTERNAL_FOLDER;
            Border externalBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    isExternalSelected ? System.Windows.Media.Color.FromRgb(225, 225, 225) : System.Windows.Media.Colors.Transparent),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(10, 2, 10, 2),
                Cursor = Cursors.Hand
            };

            TextBlock externalText = new TextBlock
            {
                Text = "🔗 外部笔记",
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
                VerticalAlignment = VerticalAlignment.Center
            };
            externalBorder.Child = externalText;

            externalBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (currentFolderPath != VIRTUAL_EXTERNAL_FOLDER)
                {
                    if (isDirty) SaveNote();
                    currentFilePath = null;
                    ShowEditor(false);
                }

                currentFolderPath = VIRTUAL_EXTERNAL_FOLDER;
                LoadFolders();
                LoadNotes(VIRTUAL_EXTERNAL_FOLDER);
            };

            FolderListPanel.Children.Add(externalBorder);

            // ==================== 顺序 3：渲染其他自定义文件夹 ====================
            foreach (string folder in otherFolders)
            {
                RenderFolderItem(folder);
            }
        }

        // 提取的普通文件夹渲染辅助逻辑
        private void RenderFolderItem(string folder)
        {
            string folderName = Path.GetFileName(folder);
            bool isDefaultFolder = folderName == "默认文件夹";
            bool isSelected = string.Equals(folder, currentFolderPath, StringComparison.OrdinalIgnoreCase);

            Border folderBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    isSelected ? System.Windows.Media.Color.FromRgb(225, 225, 225) : System.Windows.Media.Colors.Transparent),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(10, 2, 10, 2),
                Cursor = Cursors.Hand
            };

            ContextMenu ctx = new ContextMenu();
            if (!isDefaultFolder)
            {
                MenuItem deleteItem = new MenuItem { Header = "删除文件夹" };
                deleteItem.Click += (s, e) => ShowDeleteDialog(folder, true);
                ctx.Items.Add(deleteItem);
            }
            else
            {
                MenuItem lockItem = new MenuItem { Header = "系统默认 (不可修改)", IsEnabled = false };
                ctx.Items.Add(lockItem);
            }
            folderBorder.ContextMenu = ctx;

            folderBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (currentFolderPath != folder)
                {
                    if (isDirty) SaveNote();
                    currentFilePath = null;
                    ShowEditor(false);
                }

                currentFolderPath = folder;
                LoadFolders();
                LoadNotes(folder);
            };

            Grid itemGrid = new Grid();
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock iconText = new TextBlock
            {
                Text = "📁 ",
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconText, 0);

            TextBlock nameText = new TextBlock
            {
                Text = folderName,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(nameText, 1);

            TextBox editBox = new TextBox
            {
                Text = folderName,
                Visibility = Visibility.Collapsed,
                MaxLength = 64,
                FontWeight = FontWeights.SemiBold,
                FontSize = nameText.FontSize,
                Foreground = nameText.Foreground,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(editBox, 1);

            itemGrid.Children.Add(iconText);
            itemGrid.Children.Add(nameText);
            itemGrid.Children.Add(editBox);
            folderBorder.Child = itemGrid;

            if (!isDefaultFolder)
            {
                MenuItem renameItem = new MenuItem { Header = "重命名" };
                renameItem.Click += (s, e) =>
                {
                    nameText.Visibility = Visibility.Collapsed;
                    editBox.Visibility = Visibility.Visible;
                    editBox.Focus();
                    editBox.SelectAll();
                };
                ctx.Items.Insert(0, renameItem);

                editBox.LostFocus += (s, e) =>
                {
                    nameText.Visibility = Visibility.Visible;
                    editBox.Visibility = Visibility.Collapsed;

                    string newName = editBox.Text.Trim();
                    string oldName = Path.GetFileName(folder);

                    if (string.IsNullOrEmpty(newName) || newName == oldName) return;

                    if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[a-zA-Z0-9\u4e00-\u9fa5_ \-]+$"))
                    {
                        MessageBox.Show("名称包含非法特殊符号！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        editBox.Text = oldName;
                        return;
                    }

                    string newFolderPath = Path.Combine(rootWorkspacePath, newName);
                    if (Directory.Exists(newFolderPath))
                    {
                        MessageBox.Show("已存在同名文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        editBox.Text = oldName;
                        return;
                    }

                    try
                    {
                        Directory.Move(folder, newFolderPath);

                        if (currentFolderPath == folder) currentFolderPath = newFolderPath;
                        if (currentFilePath != null && currentFilePath.StartsWith(folder))
                        {
                            currentFilePath = Path.Combine(newFolderPath, Path.GetFileName(currentFilePath));
                        }

                        Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                            LoadFolders();
                            if (currentFolderPath == newFolderPath) LoadNotes(currentFolderPath);
                        }));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("重命名失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        editBox.Text = oldName;
                    }
                };

                editBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.Handled = true;
                        this.Focus();
                    }
                    else if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        editBox.Text = Path.GetFileName(folder);
                        this.Focus();
                    }
                };
            }

            FolderListPanel.Children.Add(folderBorder);
        }

        private void ShowDeleteDialog(string path, bool isFolder)
        {
            // 不允许删除默认文件夹
            if (isFolder && Path.GetFileName(path) == "默认文件夹")
            {
                MessageBox.Show("「默认文件夹」为系统保留区域，不可删除！", "拦截提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                // 在 BtnConfirmDelete_Click 方法内，修改删除文件夹的判断逻辑：
                if (isDeletingFolder && Directory.Exists(itemToDeletePath))
                {
                    Directory.Delete(itemToDeletePath, true);

                    // 【关键修复】：如果删除的文件夹就是当前文件夹，或者当前正在编辑的笔记属于这个被删的文件夹
                    if (currentFolderPath == itemToDeletePath ||
                       (currentFilePath != null && currentFilePath.StartsWith(itemToDeletePath)))
                    {
                        currentFolderPath = null;
                        currentFilePath = null; // 必须把文件路径也置空！
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

        // 监听删除确认弹窗遮罩层的点击事件
        private void DeleteConfirmDialog_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // e.OriginalSource 是鼠标真正踩中的 UI 元素
            // 如果点中的是外层半透明背景（而不是里面的白色对话框卡片或文字按钮），就自动关闭弹窗
            if (e.OriginalSource == DeleteConfirmDialog)
            {
                BtnCancelDelete_Click(sender, e);
            }
        }

        // 监听搜索框文本实时变化
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BtnClearSearch != null)
            {
                BtnClearSearch.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Collapsed : Visibility.Visible;
            }

            // 重置定时器，打字时不会疯狂触发 I/O
            searchTimer.Stop();
            searchTimer.Start();
        }

        // 点击 X 按钮清空搜索
        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";    // 这会自动触发 TextChanged 事件，恢复原始列表
            SearchTextBox.Focus();      // 保持焦点在搜索框，方便继续输入
        }

        // 搜索辅助方法：将带有格式代码的 RTF 转换为纯文本，用于精准搜索
        private string ExtractTextFromRtf(string rtf)
        {
            if (string.IsNullOrEmpty(rtf)) return "";
            try
            {
                // 在内存中虚拟一个文档，利用 WPF 自带的解析器剥离排版代码
                System.Windows.Documents.FlowDocument doc = new System.Windows.Documents.FlowDocument();
                System.Windows.Documents.TextRange range = new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd);

                using (System.IO.MemoryStream ms = new System.IO.MemoryStream(System.Text.Encoding.Default.GetBytes(rtf)))
                {
                    range.Load(ms, DataFormats.Rtf);
                }
                return range.Text;
            }
            catch
            {
                return ""; // 解析失败兜底
            }
        }

        // 文件夹侧边栏切换显示
        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            // 【防抖核心】：如果动画还在进行中，直接拦截，无视玩家的“狂点”
            if (isSidebarAnimating) return;

            isSidebarAnimating = true; // 上锁
            isSidebarOpen = !isSidebarOpen;

            System.Windows.Media.Animation.DoubleAnimation animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = isSidebarOpen ? 200 : 0,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            if (NoteEditor != null)
            {
                // 动画开始前，强行将编辑器的宽度“锁死”在当前的实际像素上
                NoteEditor.HorizontalAlignment = HorizontalAlignment.Left;
                NoteEditor.Width = NoteEditor.ActualWidth;
            }

            animation.Completed += (s, ev) =>
            {
                if (NoteEditor != null)
                {
                    // 动画完成后，解除尺寸锁定
                    NoteEditor.Width = double.NaN;
                    NoteEditor.HorizontalAlignment = HorizontalAlignment.Stretch;
                }

                // 【防抖核心】：动画彻底播完，解锁，允许下一次点击
                isSidebarAnimating = false;
            };

            SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, animation);
        }

        // 获取外部笔记历史记录配置文件路径
        private string GetExternalNotesConfigPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lume");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, "external_notes.json");
        }

        // 读取外部笔记路径列表
        private List<string> GetExternalNotePaths()
        {
            string configPath = GetExternalNotesConfigPath();
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
                catch { }
            }
            return new List<string>();
        }

        // 记录一个新的外部笔记路径（置顶展示）
        private void AddExternalNotePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            var list = GetExternalNotePaths();

            // 如果列表中已存在，先移除旧记录再放到最前面
            list.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, filePath);

            try
            {
                File.WriteAllText(GetExternalNotesConfigPath(), System.Text.Json.JsonSerializer.Serialize(list));
            }
            catch { }
        }

        // 从外部笔记历史中移除
        private void RemoveExternalNotePath(string filePath)
        {
            var list = GetExternalNotePaths();
            list.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
            try
            {
                File.WriteAllText(GetExternalNotesConfigPath(), System.Text.Json.JsonSerializer.Serialize(list));
            }
            catch { }
        }

        private void SetupVersionBadge()
        {
            // 获取当前运行程序的版本信息
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            if (VersionText != null && version != null)
            {
                // 格式化输出为主版本.次版本.内部版本（例如：v1.0.0）
                // 这样可以去掉默认的第4位修订号，看起来更精简
                VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        // 滚轮拦截事件：实现整体字号的放大与缩小
        private void EditorContainer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    _currentZoomFactor += 0.1; // 放大 10%
                }
                else if (e.Delta < 0)
                {
                    _currentZoomFactor -= 0.1; // 缩小 10%
                }

                // 限制缩放极值：50% ~ 300%
                if (_currentZoomFactor < 0.5) _currentZoomFactor = 0.5;
                if (_currentZoomFactor > 3.0) _currentZoomFactor = 3.0;

                // 1. 实时渲染字体大小
                NoteTitleBox.FontSize = BaseTitleFontSize * _currentZoomFactor;
                NoteEditor.FontSize = BaseEditorFontSize * _currentZoomFactor;

                // 实时更新底部状态栏的缩放百分比
                if (ZoomText != null) ZoomText.Text = $"{Math.Round(_currentZoomFactor * 100)}%";

                // 2. 存入当前笔记对象并标记 dirty，提醒系统保存
                if (currentNote != null)
                {
                    currentNote.ZoomFactor = _currentZoomFactor;
                    isDirty = true;
                    if (StatusText != null) StatusText.Text = "编辑中 (点击外部空白处即可保存)...";
                }

                e.Handled = true; // 拦截事件，避免文本上下滚动
            }
        }

        // 光标行列变化的处理方法
        private void Caret_PositionChanged(object sender, EventArgs e)
        {
            if (CursorPositionText != null)
            {
                int line = NoteEditor.TextArea.Caret.Line;
                int column = NoteEditor.TextArea.Caret.Column;
                CursorPositionText.Text = $"第 {line} 行，第 {column} 列";
            }
        }

        // 1. 控制悬浮菜单的弹出
        private void BtnAa_Click(object sender, RoutedEventArgs e)
        {
            // 如果没有打开笔记，阻止弹出
            if (EditorContainer.Visibility != Visibility.Visible) return;
            TextFormatPopup.IsOpen = !TextFormatPopup.IsOpen;
        }

        // 2. 核心：AvalonEdit 文本格式化包裹逻辑
        private void ApplyTextFormat(string prefix, string suffix)
        {
            if (NoteEditor == null || NoteEditor.Document == null) return;

            int selectionStart = NoteEditor.SelectionStart;
            int selectionLength = NoteEditor.SelectionLength;

            // 获取选中的文本
            string selectedText = NoteEditor.SelectedText;

            // 拼接成新的 Markdown/HTML 格式文本
            string newText = $"{prefix}{selectedText}{suffix}";

            // 使用 Document.Replace 确保操作会被计入 AvalonEdit 的撤销历史(Ctrl+Z)
            NoteEditor.Document.Replace(selectionStart, selectionLength, newText);

            // 调整光标位置
            if (selectionLength == 0)
            {
                // 如果没有选中文本，把光标停在两个符号的中间，方便用户直接打字
                NoteEditor.SelectionStart = selectionStart + prefix.Length;
            }
            else
            {
                // 如果有选中文本，包裹后直接选中全段
                NoteEditor.SelectionStart = selectionStart;
                NoteEditor.SelectionLength = newText.Length;
            }

            // 焦点回到编辑器
            NoteEditor.Focus();

            // 触发保存状态更新
            isDirty = true;
            if (StatusText != null) StatusText.Text = "编辑中 (点击外部空白处即可保存)...";
        }

        // 3. 绑定各格式按钮的点击事件 (使用标准的 Markdown 和 HTML 语法)
        private void BtnFormatBold_Click(object sender, RoutedEventArgs e) => ApplyTextFormat("**", "**");
        private void BtnFormatItalic_Click(object sender, RoutedEventArgs e) => ApplyTextFormat("*", "*");
        private void BtnFormatUnderline_Click(object sender, RoutedEventArgs e) => ApplyTextFormat("<u>", "</u>"); // Markdown 原生没有下划线，通常借用 HTML
        private void BtnFormatStrikethrough_Click(object sender, RoutedEventArgs e) => ApplyTextFormat("~~", "~~");
    }
}