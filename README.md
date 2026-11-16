# Flash - Folder（FF）

**闪电般定位大文件，用 AI 看懂目录，快速制定清理方案。**

Flash - Folder 是 Windows 磁盘空间分析工具。选一个目录、一个盘符或整个电脑，即可找到空间主要用在哪，并把扫描证据交给 AI 进一步分析。

![Flash - Folder 桌面界面，展示目录树、大小列表、AI 分析和导出入口](docs/images/ff-desktop.png)

*新版界面。路径、统计和 AI 回复为演示数据。*

## 三个核心能力

1. **闪电速度扫描，快速找大文件。** 扫描时逐步显示目录。快速估算可优先找超过自定义门槛的文件，适合先定位占空间的大项。需要精确汇总时，把门槛设为 0，纳入所有文件。可选统计实际占用，并按硬链接去重。
2. **AI 理解目录内容，方便快速清理。** 选中文件或目录后，可让 AI 根据扫描中的名称、路径、结构和大小推断用途，整理优先检查项、可疑缓存和需要保留的内容。支持配置文字模型，也可调用本机 Codex CLI。清理决策由用户确认。
3. **导出报告，交给 Codex、DSH 等制定全面清理方案。** 点「导出报告」保存 Markdown 分析摘要，交给你的 AI 工具做跨盘归纳、风险判断和清理排序。也可导出 JSON、CSV、XML，供其他工具处理或重新打开扫描快照。

## 从源码运行

需要 Windows 和 .NET 10 SDK。运行现成桌面构建需要 .NET 10 Desktop Runtime。

```powershell
dotnet run --project src/FolderScope.Desktop/FolderScope.Desktop.csproj -c Release
dotnet run --project src/FolderScope.Cli/FolderScope.Cli.csproj -c Release -- --help
dotnet run --project tests/FolderScope.ModelTests/FolderScope.ModelTests.csproj -c Release
```

模型测试默认使用模拟请求。核心测试包含开发机专用回归和压力场景，运行前请查看入口参数。
