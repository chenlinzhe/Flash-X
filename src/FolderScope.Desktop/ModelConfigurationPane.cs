using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FolderScope.Desktop;

internal sealed class ModelConfigurationPane : Grid
{
    private readonly TextBox _query = new() { ToolTip = "搜索厂商、名称或模型 ID" };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _name = new();
    private readonly TextBox _url = new();
    private readonly TextBox _model = new();
    private readonly TextBox _command = new();
    private readonly ComboBox _protocol = new() { ItemsSource = new[] { "openai-chat", "openai-responses", "ant-messages", "codex-cli" } };
    private readonly PasswordBox _key = new();
    private readonly CheckBox _forget = new() { Content = "清除该厂商所有模型的密钥", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _keyHint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _active = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _test = Button("测试连接");
    private readonly Button _default = Button("设为默认");
    private readonly HttpClient? _testClient;
    private List<ModelProfile> _profiles;
    private string? _selectedKey;
    private bool _refreshing;
    private CancellationTokenSource? _testCancellation;
    private string _defaultKey;

    internal Func<string, bool>? SaveDefaultRequested { get; set; }

    public ModelConfigurationPane(ModelSettings settings, HttpClient? testClient = null)
    {
        _testClient = testClient;
        _profiles = settings.ModelProfiles!.ToList();
        _defaultKey = settings.DefaultProfileKey!;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275), MinWidth = 190 });
        ColumnDefinitions.Add(new ColumnDefinition());
        var catalog = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var add = Button("添加自定义模型");
        SetTop(catalog, _query);
        SetTop(catalog, add);
        ModelGrouping.Configure(_list, () => _defaultKey);
        catalog.Children.Add(_list);
        Children.Add(catalog);

        var editor = new StackPanel { Margin = new Thickness(2, 0, 0, 0) };
        editor.Children.Add(new TextBlock { Text = "文字模型", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        editor.Children.Add(_active);
        void Field(string title, Control input)
        {
            editor.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 7, 0, 4) });
            System.Windows.Automation.AutomationProperties.SetName(input, title);
            editor.Children.Add(input);
        }
        Field("显示名称", _name);
        Field("模型 ID", _model);
        Field("API 地址", _url);
        Field("接口协议", _protocol);
        Field("Codex CLI 路径", _command);
        Field("厂商共享 API 密钥", _key);
        editor.Children.Add(_forget);
        editor.Children.Add(_keyHint);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(_test);
        actions.Children.Add(_default);
        editor.Children.Add(actions);
        editor.Children.Add(_status);
        var scroll = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SetColumn(scroll, 1);
        Children.Add(scroll);
        System.Windows.Automation.AutomationProperties.SetName(_query, "搜索文字模型");
        System.Windows.Automation.AutomationProperties.SetName(_list, "文字模型列表");

        _list.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _list.SelectedItem is not ModelProfile profile) return;
            CommitEditor();
            ShowProfile(_profiles.First(item => item.Key == profile.Key));
        };
        _query.TextChanged += (_, _) => { CommitEditor(); RefreshList(); };
        add.Click += (_, _) =>
        {
            CommitEditor();
            var provider = _profiles.FirstOrDefault(item => item.Key == _selectedKey);
            var item = new ModelProfile("custom:" + Guid.NewGuid().ToString("N"), provider?.ProviderId ?? "custom",
                provider?.ProviderName ?? "自定义", "自定义模型", provider?.BaseUrl ?? "https://api.deepseek.com/v1", "",
                provider?.ClientType ?? "openai-chat", IsCustom: true);
            _profiles.Add(item);
            _query.Clear();
            _selectedKey = item.Key;
            RefreshList();
            ShowProfile(item);
            _model.Focus();
        };
        _forget.Checked += (_, _) => _key.Clear();
        _key.PasswordChanged += (_, _) =>
        {
            if (_key.Password.Length > 0) _forget.IsChecked = false;
            CancelTestResult();
        };
        foreach (var input in new[] { _url, _model, _name, _command }) input.TextChanged += (_, _) => CancelTestResult();
        _protocol.SelectionChanged += (_, _) => CancelTestResult();
        _forget.Unchecked += (_, _) => CancelTestResult();
        _test.Click += async (_, _) => await TestAsync();
        _default.Click += (_, _) =>
        {
            try
            {
                var snapshot = EditedProfile();
                snapshot.Validate(requireKey: true);
                if (SaveDefaultRequested?.Invoke(snapshot.Key) == true)
                    SetStatus("默认模型已保存并生效。", success: true);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { SetStatus(ex.Message); }
        };
        _selectedKey = _defaultKey;
        RefreshList();
        ShowProfile(_profiles.First(item => item.Key == _defaultKey));
        AcceptSaved(settings);
    }

    internal ModelSettings Snapshot(ModelSettings basis, string? requestedDefault = null)
    {
        CommitEditor();
        var selected = _profiles.First(item => item.Key == _selectedKey);
        selected.Validate();
        return basis.WithProfiles(_profiles.ToList(), requestedDefault);
    }

    internal void AcceptSaved(ModelSettings settings)
    {
        _defaultKey = settings.DefaultProfileKey!;
        _profiles = settings.ModelProfiles!.ToList();
        if (!_profiles.Any(profile => profile.Key == _selectedKey)) _selectedKey = settings.DefaultProfileKey;
        RefreshList();
        ShowProfile(_profiles.First(profile => profile.Key == _selectedKey));

        _active.Text = "当前默认 · " + settings.EffectiveProfile.Label + " · " + settings.EffectiveProfile.ClientType;
    }

    internal void CancelTest() => _testCancellation?.Cancel();

    private ModelProfile EditedProfile()
    {
        var profile = _profiles.First(item => item.Key == _selectedKey);
        var next = profile with
        {
            DisplayName = _name.Text.Trim(), BaseUrl = _url.Text.Trim().TrimEnd('/'),
            Model = _model.Text.Trim(), ClientType = _protocol.SelectedItem as string ?? "openai-chat",
            CodexCommand = _command.Text.Trim()
        };
        var entered = _key.Password.Trim();
        return next with
        {
            ProtectedKey = _forget.IsChecked == true ? "" : entered.Length == 0 ? profile.ProtectedKey : ModelSettings.EncryptKey(entered)
        };
    }

    private void CommitEditor()
    {
        if (_selectedKey is null) return;
        var index = _profiles.FindIndex(item => item.Key == _selectedKey);
        var edited = EditedProfile();
        _profiles[index] = edited;
        _profiles = _profiles.Select(profile => profile.ProviderId == edited.ProviderId
            ? profile with { ProtectedKey = edited.ProtectedKey } : profile).ToList();
    }

    private void RefreshList()
    {
        _refreshing = true;
        try
        {
            var query = _query.Text.Trim();
            _list.ItemsSource = ModelGrouping.View(_profiles.Where(item =>
                (item.ProviderName + " " + item.DisplayName + " " + item.Model).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());
            _list.SelectedItem = _profiles.FirstOrDefault(item => item.Key == _selectedKey);
        }
        finally { _refreshing = false; }
    }

    private void ShowProfile(ModelProfile profile)
    {
        CancelTest();
        _selectedKey = profile.Key;
        _name.Text = profile.DisplayName;
        _url.Text = profile.BaseUrl;
        _model.Text = profile.Model;
        _command.Text = profile.CodexCommand;
        var local = profile.ClientType == "codex-cli";
        _command.IsEnabled = local;
        _url.IsEnabled = _key.IsEnabled = _forget.IsEnabled = !local;
        _model.ToolTip = local ? "留空使用 CLI 内置默认模型（独立于全局开发配置）" : "填写服务支持的模型 ID";
        _protocol.SelectedItem = profile.ClientType;
        _key.Clear();
        _forget.IsChecked = false;
        _keyHint.Text = profile.ProtectedKey.Length > 0 ? "该厂商已有密钥，所有同类模型共享。留空沿用，输入新值替换。" : "为该厂商填写一次密钥，同类模型即可共享。";
        if (local) _keyHint.Text = "沿用本机登录及 Windows 代理；模型 ID 留空使用 CLI 内置默认，不继承全局开发配置。";
        SetStatus("");
    }

    private async Task TestAsync()
    {
        using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        CancelTest();
        _testCancellation = pending;
        _test.IsEnabled = false;
        try
        {
            // Capture control values before the first await, including the last key stroke.
            var snapshot = EditedProfile();
            snapshot.Validate(requireKey: true);
            SetStatus("正在测试当前编辑值…");
            var clock = Stopwatch.StartNew();
            if (snapshot.ClientType == "codex-cli")
                await LocalCodexClient.TestAsync(snapshot, pending.Token);
            else
                await TextModelClient.SendAsync(snapshot, "请只回复 OK。",
                    new[] { new AnalysisChat.Turn("user", "连接测试，请回复 OK。") }, pending.Token, _testClient);
            if (!pending.IsCancellationRequested) SetStatus(snapshot.ClientType == "codex-cli" ? "连接成功，已收到本机 Codex 实际回复。" : $"连接成功 · {clock.ElapsedMilliseconds:N0} ms", success: true);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_testCancellation, pending))
                SetStatus("测试已取消或超时，请重试。");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or FormatException
            or JsonException or System.ComponentModel.Win32Exception)
        { if (!pending.IsCancellationRequested) SetStatus(ex.Message); }
        finally
        {
            if (ReferenceEquals(_testCancellation, pending))
            {
                _testCancellation = null;
                _test.IsEnabled = true;
            }
        }
    }

    private void CancelTestResult()
    {
        CancelTest();
        SetStatus("");
    }

    private void SetStatus(string text, bool success = false)
    {
        _status.Text = text;
        _status.Foreground = success ? Brushes.SeaGreen : Brushes.DarkRed;
    }

    private static Button Button(string label) => new() { Content = label, Margin = new Thickness(0, 8, 8, 0), Padding = new Thickness(10, 4, 10, 4) };
    private static void SetTop(DockPanel panel, UIElement child)
    {
        DockPanel.SetDock(child, Dock.Top);
        panel.Children.Add(child);
    }
}
