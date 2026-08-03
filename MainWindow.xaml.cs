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

            // 3. 【修改这里】：强制传入 null，确保启动时绝对不会选中和打开任何笔记
            SetupEnvironment(null);
        }

        public MainWindow(string openedFilePath)
        {
            InitializeComponent();
            SetupEnvironment(openedFilePath);
        }

        private void SetupEnvironment(string openedFilePath)
        {
            currentFilePath = openedFilePath;

            // 【强制拦截 1】：确保 LumeWorkspace 里必须有文件夹，如果没有，必须建一个！
            string[] folders = Directory.GetDirectories(rootWorkspacePath);
            if (folders.Length == 0)
            {
                Directory.CreateDirectory(Path.Combine(rootWorkspacePath, "默认文件夹"));
                folders = Directory.GetDirectories(rootWorkspacePath);
            }

            // 【强制拦截 2】：判断打开的笔记到底是不是在我们合法的文件夹里？
            if (!string.IsNullOrEmpty(currentFilePath) && File.Exists(currentFilePath))
            {
                string noteFolder = Path.GetDirectoryName(currentFilePath);

                if (noteFolder.StartsWith(rootWorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    currentFolderPath = noteFolder;
                }
                else
                {
                    currentFolderPath = folders[0];
                }
            }
            else
            {
                // 正常启动（传入null时走到这里）
                // 【修改这里】：如果读出来的 currentFolderPath 为空，或者文件夹已经被删了、不合法，才强制退回到第一个文件夹
                if (string.IsNullOrEmpty(currentFolderPath) ||
                    !Directory.Exists(currentFolderPath) ||
                    !currentFolderPath.StartsWith(rootWorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    currentFolderPath = folders[0];
                }
            }

            // 【强制拦截 3】：为了防止路径大小写不一致导致高亮失败，做一次严格匹配
            bool isValid = false;
            foreach (string f in folders)
            {
                if (string.Equals(f, currentFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    currentFolderPath = f;
                    isValid = true;
                    break;
                }
            }
            if (!isValid) currentFolderPath = folders[0]; // 终极兜底：强行绑定！

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

        private void BtnAddNote_Click(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(currentFolderPath))
            {
                MessageBox.Show("请先在左侧选择或创建一个文件夹！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // lumeYYMMDDxx 命名格式
            string dateStr = DateTime.Now.ToString("yyMMdd");
            int seq = 1;
            string newFilePath;

            while (true)
            {
                // 保证两位数序号，例如 01, 02
                string seqStr = seq.ToString("D2");
                newFilePath = Path.Combine(currentFolderPath, $"lume{dateStr}{seqStr}.lume");

                if (!File.Exists(newFilePath))
                {
                    break; // 找到未被占用的文件名，跳出循环
                }
                seq++;
            }

            // 创建空文件并保存
            NoteData newNote = new NoteData { Title = "新笔记", DateCreated = DateTime.Now.ToString("yyyy/MM/dd") };
            LumeFileManager.SaveLumeFile(newFilePath, newNote);

            // 【核心修复4】：必须先告诉系统 currentFilePath 是谁，再去刷新列表
            currentFilePath = newFilePath;
            LoadNote();
            ShowEditor(true);

            LoadNotes(currentFolderPath); // 现在刷新列表，它就会被正确高亮，且连带标题联动！

            NoteTitleBox.Focus();       // 聚焦到标题
            NoteTitleBox.SelectAll();   // 全选"新笔记"三个字
        }

        private void LoadNotes(string folderPath)
        {
            NoteListPanel.Children.Clear();
            if (!Directory.Exists(folderPath)) return;

            // 获取文件列表并按【创建时间倒序】排列，最新创建的在最前面
            var noteFiles = Directory.GetFiles(folderPath, "*.lume")
                                     .Select(f => new FileInfo(f))
                                     .OrderByDescending(f => f.CreationTime)
                                     .Select(f => f.FullName);

            foreach (string file in noteFiles)
            {
                NoteData noteData = LumeFileManager.OpenLumeFile(file);

                // 检查当前循环到的笔记，是不是正在编辑的那个
                bool isSelected = (file == currentFilePath);

                Border cardBorder = new Border
                {
                    // 如果是正在编辑的笔记，使用最浅的淡白色，否则透明（没有背景）
                    Background = new System.Windows.Media.SolidColorBrush(
                        isSelected ? System.Windows.Media.Color.FromRgb(243, 243, 243) : System.Windows.Media.Colors.Transparent),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 0, 0, 10),
                    Cursor = Cursors.Hand
                };

                ContextMenu ctx = new ContextMenu();
                MenuItem deleteItem = new MenuItem { Header = "删除笔记" };
                deleteItem.Click += (s, e) => ShowDeleteDialog(file, false);
                ctx.Items.Add(deleteItem);
                cardBorder.ContextMenu = ctx;

                cardBorder.MouseLeftButtonDown += (s, e) =>
                {
                    currentFilePath = file;
                    LoadNote();
                    ShowEditor(true);
                    LoadNotes(folderPath); // 点击其他笔记时，重新刷新列表以更新高亮
                };

                StackPanel cardStack = new StackPanel();

                // 【之前漏掉的补在这里】：创建日期文本
                TextBlock dateText = new TextBlock
                {
                    Text = noteData.DateCreated,
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

                TextBox titleEditBox = new TextBox
                {
                    Text = noteData.Title,
                    Visibility = Visibility.Collapsed,
                    MaxLength = 64, // 限制最大长度64字
                    FontWeight = FontWeights.Bold,
                    BorderThickness = new Thickness(0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    Margin = new Thickness(-2, 0, 0, 0)
                };

                Grid titleGrid = new Grid();
                titleGrid.Children.Add(titleText);
                titleGrid.Children.Add(titleEditBox);

                // 笔记重命名右键菜单
                MenuItem renameNoteItem = new MenuItem { Header = "重命名" };
                renameNoteItem.Click += (s, e) =>
                {
                    titleText.Visibility = Visibility.Collapsed;
                    titleEditBox.Visibility = Visibility.Visible;
                    titleEditBox.Focus();
                    titleEditBox.SelectAll();
                };
                ctx.Items.Insert(0, renameNoteItem);

                // 失去焦点（点击外部）时自动保存标题
                titleEditBox.LostFocus += (s, e) =>
                {
                    titleText.Visibility = Visibility.Visible;
                    titleEditBox.Visibility = Visibility.Collapsed;

                    string newName = titleEditBox.Text.Trim();
                    if (string.IsNullOrEmpty(newName) || newName == noteData.Title)
                    {
                        titleEditBox.Text = noteData.Title;
                        return;
                    }

                    if (!Regex.IsMatch(newName, @"^[a-zA-Z0-9\u4e00-\u9fa5_ \-]+$"))
                    {
                        MessageBox.Show("笔记名称包含非法特殊符号！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        titleEditBox.Text = noteData.Title;
                        return;
                    }

                    // 只修改内部的标题，不修改底层物理文件名（保持lumeYYMMDD不变）
                    noteData.Title = newName;
                    LumeFileManager.SaveLumeFile(file, noteData);
                    titleText.Text = newName;

                    // 如果重命名的是当前正打开的笔记，右侧大标题同步改变
                    if (file == currentFilePath && NoteTitleBox != null)
                    {
                        NoteTitleBox.Text = newName;
                    }
                };

                // 按键支持：按 Enter 保存，按 Esc 取消
                titleEditBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.Handled = true; // 阻止默认按键行为
                        this.Focus();     // 强行把焦点转移给主窗口，100% 触发 LostFocus 执行保存
                    }
                    else if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        titleEditBox.Text = noteData.Title; // 恢复原名
                        this.Focus();     // 移走焦点取消编辑
                    }
                };

                if (isSelected)
                {
                    currentNoteListTitleUI = titleText;
                }

                // 组合卡片内容：先放入支持编辑的标题，再放入日期
                cardStack.Children.Add(titleGrid);
                cardStack.Children.Add(dateText);

                cardBorder.Child = cardStack;
                NoteListPanel.Children.Add(cardBorder);
            }
        }

        private void LoadNote()
        {
            currentNote = LumeFileManager.OpenLumeFile(currentFilePath);
            NoteTitleBox.Text = currentNote.Title;

            // 【必须添加的致命修复】：在加载任何内容前，先清空旧内容！
            NoteEditor.Document.Blocks.Clear();

            if (!string.IsNullOrEmpty(currentNote.ContentRtf))
            {
                using (MemoryStream ms = new MemoryStream(System.Text.Encoding.Default.GetBytes(currentNote.ContentRtf)))
                {
                    TextRange textRange = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd);
                    textRange.Load(ms, DataFormats.Rtf);
                }
            }
            else
            {
                // 【细节修复】：如果是空笔记，必须塞入一个空段落，强制清空边距
                NoteEditor.Document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
            }

            isDirty = false;
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

            isDirty = false; // 保存成功，恢复干净状态

            // 【核心修复1】：保存成功后，强行刷新一遍中间的笔记列表！
            if (!string.IsNullOrEmpty(currentFolderPath))
            {
                LoadNotes(currentFolderPath);
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
                // 把当前选中的文件夹路径存起来
                if (!string.IsNullOrEmpty(currentFolderPath))
                {
                    File.WriteAllText(GetConfigPath(), currentFolderPath);
                }

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

            string[] folders = Directory.GetDirectories(rootWorkspacePath);
            foreach (string folder in folders)
            {
                // 判断当前循环到的文件夹，是不是我们选中的文件夹
                bool isSelected = string.Equals(folder, currentFolderPath, StringComparison.OrdinalIgnoreCase);

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

                // 在 LoadFolders() 方法内，修改 folderBorder.MouseLeftButtonDown：
                folderBorder.MouseLeftButtonDown += (s, e) =>
                {
                    // 如果点击的是不同的文件夹，必须先处理当前正在编辑的笔记
                    if (currentFolderPath != folder)
                    {
                        if (isDirty) SaveNote(); // 如果有修改，先保存
                        currentFilePath = null;  // 清除当前文件路径
                        ShowEditor(false);       // 关闭编辑器，显示“单击此处新建笔记”
                    }

                    currentFolderPath = folder;
                    LoadFolders();
                    LoadNotes(folder);
                };

                TextBlock text = new TextBlock
                {
                    Text = "📁 " + Path.GetFileName(folder),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51))
                };

                TextBox editBox = new TextBox
                {
                    Text = Path.GetFileName(folder),
                    Visibility = Visibility.Collapsed,
                    MaxLength = 64, // 限制最大长度64字
                    FontWeight = FontWeights.SemiBold,
                    BorderThickness = new Thickness(0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid itemGrid = new Grid();
                itemGrid.Children.Add(text);
                itemGrid.Children.Add(editBox);
                folderBorder.Child = itemGrid;

                // 重命名右键菜单项
                MenuItem renameItem = new MenuItem { Header = "重命名" };
                renameItem.Click += (s, e) =>
                {
                    text.Visibility = Visibility.Collapsed;
                    editBox.Visibility = Visibility.Visible;
                    editBox.Focus();
                    editBox.SelectAll();
                };
                ctx.Items.Insert(0, renameItem); // 插入到右键菜单最上面

                // 失去焦点（点击外面）或按下回车时，执行保存逻辑
                editBox.LostFocus += (s, e) =>
                {
                    text.Visibility = Visibility.Visible;
                    editBox.Visibility = Visibility.Collapsed;

                    string newName = editBox.Text.Trim();
                    string oldName = Path.GetFileName(folder);

                    if (string.IsNullOrEmpty(newName) || newName == oldName) return;

                    // 正则过滤：只允许汉字、字母、数字、空格、下划线、连字符
                    if (!Regex.IsMatch(newName, @"^[a-zA-Z0-9\u4e00-\u9fa5_ \-]+$"))
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
                        Directory.Move(folder, newFolderPath); // 修改本地文件夹名称

                        // 如果重命名的是当前正在使用的文件夹，同步更新全局变量
                        if (currentFolderPath == folder) currentFolderPath = newFolderPath;
                        if (currentFilePath != null && currentFilePath.StartsWith(folder))
                        {
                            currentFilePath = Path.Combine(newFolderPath, Path.GetFileName(currentFilePath));
                        }

                        // 延迟刷新以防止 UI 线程冲突
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

                // 按键支持：按 Enter 保存，按 Esc 取消
                editBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.Handled = true; // 阻止默认按键行为（避免发出提示音）
                        this.Focus();     // 强行把焦点转移给主窗口，100% 触发 LostFocus 执行保存
                    }
                    else if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        editBox.Text = Path.GetFileName(folder); // 恢复原名
                        this.Focus();     // 移走焦点取消编辑
                    }
                };

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
    }
}