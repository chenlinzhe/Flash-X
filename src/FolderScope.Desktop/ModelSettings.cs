using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FolderScope.Desktop;

internal sealed partial record ModelSettings(string BaseUrl = "https://api.deepseek.com", string Model = "deepseek-flash", string ProtectedKey = "", string? AnalysisPrompt = null)
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderScope", "model-settings.json");

    public static ModelSettings Load() => Read(SettingsPath);

    internal string EffectiveAnalysisPrompt => string.IsNullOrWhiteSpace(AnalysisPrompt) ? CleanupAnalysisPrompt.Text : AnalysisPrompt;

    public Uri Endpoint() => EffectiveProfile.Endpoint();

    public static void Edit(Window owner)
    {
        ModelSettings current;
        try { current = Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { MessageBox.Show(owner, "无法读取模型设置：" + ex.Message, "模型设置"); return; }

        var dialog = new Window { Owner = owner, Title = "设置", Width = 1060, Height = 720, MinWidth = 900,
            MinHeight = 490, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Resources = InterfaceTheme.Create() };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = layout;

        var navigation = new ListBox { BorderThickness = new Thickness(0), Padding = new Thickness(10, 16, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(240, 245, 248)) };
        navigation.Items.Add(new ListBoxItem { Content = "模型设置", Padding = new Thickness(10, 10, 6, 10) });
        navigation.Items.Add(new ListBoxItem { Content = "AI 分析提示词", Padding = new Thickness(10, 10, 6, 10) });
        navigation.Items.Add(new ListBoxItem { Content = "软件说明", Padding = new Thickness(10, 10, 6, 10) });
        Grid.SetRowSpan(navigation, 2);
        layout.Children.Add(navigation);

        var content = new Grid { Margin = new Thickness(24, 18, 24, 0) };
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);

        var modelPage = new ModelConfigurationPane(current);
        content.Children.Add(modelPage);

        var promptPage = new DockPanel { Visibility = Visibility.Collapsed };
        content.Children.Add(promptPage);
        var promptHeading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        promptHeading.Children.Add(new TextBlock { Text = "AI 分析提示词", FontSize = 20, FontWeight = FontWeights.SemiBold });
        promptHeading.Children.Add(new TextBlock { Text = "保存后，下一次发送给模型时会使用这里的提示词。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        DockPanel.SetDock(promptHeading, Dock.Top);
        promptPage.Children.Add(promptHeading);

        var prompt = new TextBox { Text = current.EffectiveAnalysisPrompt, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(10, 8, 10, 8) };
        System.Windows.Automation.AutomationProperties.SetName(prompt, "系统提示词正文");
        var restore = new Button { Content = "恢复默认", Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        restore.Click += (_, _) => prompt.Text = CleanupAnalysisPrompt.Text;
        DockPanel.SetDock(restore, Dock.Bottom);
        promptPage.Children.Add(restore);
        promptPage.Children.Add(prompt);

        var help = new StackPanel();
        help.Children.Add(new TextBlock { Text = "软件说明", FontSize = 20, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16) });
        foreach (var paragraph in new[]
        {
            "大小表示逻辑文件长度。快速估算只累计严格大于输入门槛的文件，目录显示的是这些文件的累计值。",
            "占用空间可在扫描菜单开启。硬链接去重后的占用归首次扫描路径，其他链接路径不会重复累计。",
            "压缩或稀疏文件的实际占用可能为 0。大小未知表示尚未完整读取，部分大小为已经读取的下限。",
            "软件启动后先显示上次扫描概览，随后恢复完整目录。快照记录的是扫描时的状态。",
            "AI 分析会发送当前目标的扫描摘要和本轮对话。可在「模型设置」配置服务，在「AI 分析提示词」修改系统提示词。"
        })
            help.Children.Add(new TextBlock { Text = paragraph, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14), LineHeight = 23 });
        var helpPage = new ScrollViewer { Content = help, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed };
        content.Children.Add(helpPage);

        var footer = new DockPanel { Margin = new Thickness(24, 12, 24, 18), LastChildFill = true };
        Grid.SetColumn(footer, 1);
        Grid.SetRow(footer, 1);
        layout.Children.Add(footer);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(16, 4, 16, 4) };
        var save = new Button { Content = "保存设置", IsDefault = true, Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkRed,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        footer.Children.Add(status);

        navigation.SelectionChanged += (_, _) =>
        {
            var showPrompt = navigation.SelectedIndex == 1;
            content.Children[0].Visibility = navigation.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            helpPage.Visibility = navigation.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            promptPage.Visibility = showPrompt ? Visibility.Visible : Visibility.Collapsed;
        };
        navigation.SelectedIndex = 0;
        bool Persist(string? requestedDefault)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prompt.Text))
                {
                    navigation.SelectedIndex = 1;
                    prompt.Focus();
                    throw new ArgumentException("请输入 AI 分析提示词，或点击「恢复默认」。");
                }
                var customPrompt = string.Equals(prompt.Text.ReplaceLineEndings("\n"),
                    CleanupAnalysisPrompt.Text.ReplaceLineEndings("\n"), StringComparison.Ordinal) ? null : prompt.Text;
                var snapshot = modelPage.Snapshot(current, requestedDefault) with { AnalysisPrompt = customPrompt };
                current = snapshot.Save();
                modelPage.AcceptSaved(current);
                status.Foreground = Brushes.SeaGreen;
                status.Text = "设置已保存。";
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                or InvalidOperationException or JsonException or Win32Exception)
            {
                status.Foreground = Brushes.DarkRed;
                status.Text = "保存失败：" + ex.Message;
                return false;
            }
        }
        modelPage.SaveDefaultRequested = key => Persist(key);
        save.Click += (_, _) => { if (Persist(null)) dialog.DialogResult = true; };
        dialog.Closed += (_, _) => modelPage.CancelTest();
        dialog.ShowDialog();
    }

    public Task<string> Analyze(string material, IReadOnlyList<AnalysisChat.Turn> turns, CancellationToken token, HttpClient? client = null)
    {
        var messages = new[] { new AnalysisChat.Turn("user", "当前目标的扫描资料：\n" + material) }.Concat(turns).ToArray();
        return EffectiveProfile.ClientType == "codex-cli"
            ? LocalCodexClient.AnalyzeAsync(EffectiveProfile, EffectiveAnalysisPrompt, messages, token)
            : TextModelClient.SendAsync(EffectiveProfile, EffectiveAnalysisPrompt, messages, token, client);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob source, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob result);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob source, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob result);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr buffer);

    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var source = new Blob { Length = bytes.Length, Data = pin.AddrOfPinnedObject() };
            var ok = encrypt ? CryptProtectData(ref source, 0, 0, 0, 0, 1, out var result)
                             : CryptUnprotectData(ref source, 0, 0, 0, 0, 1, out result);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var output = new byte[result.Length]; Marshal.Copy(result.Data, output, 0, output.Length); return output; }
            finally
            {
                if (!encrypt && result.Length > 0) Marshal.Copy(new byte[result.Length], 0, result.Data, result.Length);
                LocalFree(result.Data);
            }
        }
        finally { pin.Free(); if (encrypt) CryptographicOperations.ZeroMemory(bytes); }
    }
}
