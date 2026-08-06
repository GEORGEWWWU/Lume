<div align="center">

<img src="./Assets/lume.ico" alt="Lume" width="140" />

<h1>Lume</h1>
<p>一款专为 Windows 桌面环境打造的本地化笔记应用</p>

[![.NET](https://img.shields.io/badge/.NET-10-blueviolet?logo=dotnet)](https://dotnet.microsoft.com)
[![WPF](https://img.shields.io/badge/WPF-Desktop-0078D4?logo=microsoft)](https://learn.microsoft.com/dotnet/desktop/wpf)
[![AvalonEdit](https://img.shields.io/badge/AvalonEdit-6.3-6E44FF)](https://github.com/icsharpcode/AvalonEdit)
[![License](https://img.shields.io/badge/License-MIT-green)](./LICENSE)

</div>

![1.0](./Assets/Lume%201.0%20背景图.png)

## 项目简介

Lume 是一款轻量、现代化的本地笔记软件，基于 WPF 和 AvalonEdit 构建，专注于提供流畅的桌面编辑体验。它适合用于日常记录、待办事项、灵感收集、知识整理以及个人文档管理。

项目当前版本为 1.0.0，支持在 Windows 上离线使用，并且将笔记数据保存在本机，适合对隐私和稳定性有要求的用户。

## 主要功能

- 支持文件夹分类管理笔记
- 支持新建、删除、重命名文件夹和笔记
- 支持 Markdown 风格的文本编辑与格式化按钮
- 支持待办事项渲染，点击复选框即可切换状态
- 支持 emoji 显示与实时渲染
- 支持全文搜索，按标题和内容检索笔记
- 支持 Ctrl+S 保存、Ctrl+滚轮缩放字号
- 支持从外部文件打开笔记，并记录外部笔记历史
- 支持单实例运行，避免重复打开多个窗口
- 支持将 .lume 文件关联到应用，双击即可直接打开

## 项目结构

- App.xaml / App.xaml.cs：应用启动与全局初始化逻辑
- MainWindow.xaml / MainWindow.xaml.cs：主界面、编辑器、笔记列表、文件夹管理与交互逻辑
- NoteData.cs：笔记数据模型
- LumeFileManager.cs：将笔记保存为 .lume 压缩文件并加载
- EmojiElementGenerator.cs：emoji 渲染器
- TodoElementGenerator.cs：待办事项可视化渲染器
- Assets/：应用图标等资源文件

## 运行方式

要求：Windows 操作系统，以及 .NET 10 SDK。

1. 安装 .NET 10 SDK
2. 在项目根目录执行：

```bash
dotnet restore
dotnet build
dotnet run --project Lume.csproj
```

如果你使用 Visual Studio，也可以直接打开解决方案文件 Lume.slnx 后构建运行。

## 数据存储说明

项目会在本机文档目录下创建工作区文件夹，默认位置如下：

- 文档目录 /LumeWorkspace
- 笔记文件以 .lume 格式保存
- 笔记内容会以压缩包形式存储，便于本地管理

## 许可证

本项目采用 MIT License，详情请查看 [LICENSE](./LICENSE)。