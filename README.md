# FolderScope

FolderScope 是 Windows 文件和目录大小分析工具。它提供目录树、按需读取的文件明细、大小筛选、扫描进度和取消。扫描可以选择一个目录、一个盘符或整个电脑。历史记录保留最近三次结果，并支持分层预览。

- 逻辑大小按文件路径累计。可在扫描菜单启用实际占用统计，并对硬链接去重。
- 快速估算按设定门槛累计文件。界面会区分零字节、大小未知和部分结果。
- 可选 AI 分析使用用户配置的模型或本机 Codex CLI，由用户发起请求。
- 现有版本适用于 Windows，需安装 .NET 10 Desktop Runtime。开发构建需要 .NET 10 SDK。

## 从源码构建

在项目根目录打开 PowerShell：

```powershell
dotnet run --project src/FolderScope.Desktop/FolderScope.Desktop.csproj -c Release
dotnet run --project src/FolderScope.Cli/FolderScope.Cli.csproj -c Release -- --help
dotnet run --project tests/FolderScope.ModelTests/FolderScope.ModelTests.csproj -c Release
```

模型测试默认使用模拟请求。核心测试项目还含有开发机专用回归和压力场景，运行前请查看其入口参数与样本限制。
