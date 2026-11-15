using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FolderScope.Desktop;

internal sealed class ModelSelector : ComboBox
{
    private readonly Func<ModelSettings> _read;
    private readonly Func<ModelSettings, ModelSettings> _save;
    private bool _refreshing;
    internal event Action<string>? Failure;
    internal event Action<ModelSettings>? DefaultChanged;

    internal ModelSelector(Func<ModelSettings>? read = null, Func<ModelSettings, ModelSettings>? save = null)
    {
        _read = read ?? ModelSettings.Load;
        _save = save ?? (settings => settings.Save());
        SetResourceReference(StyleProperty, "RoundedComboBox");
        Width = 196;
        Height = 32;
        VerticalAlignment = VerticalAlignment.Center;
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Label"));
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        ItemTemplate = new DataTemplate { VisualTree = label };
        ToolTip = "选择模型并保存为默认模型";
        System.Windows.Automation.AutomationProperties.SetName(this, "当前默认模型");
        SelectionChanged += (_, _) =>
        {
            if (_refreshing || SelectedItem is not ModelProfile selected) return;
            try
            {
                var latest = _read();
                var profile = latest.ModelProfiles!.SingleOrDefault(item => item.Key == selected.Key)
                    ?? throw new InvalidOperationException("该模型配置已变化，请重新选择。");
                profile.Validate(requireKey: true);
                var saved = _save(latest.WithProfiles(latest.ModelProfiles!, profile.Key));
                Display(saved);
                DefaultChanged?.Invoke(saved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or JsonException or Win32Exception)
            {
                RefreshModels();
                Failure?.Invoke("默认模型未保存：" + ex.Message);
            }
        };
        Loaded += (_, _) => RefreshModels();
    }

    internal void RefreshModels()
    {
        try { Display(_read()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or JsonException)
        { Failure?.Invoke("模型配置未读取：" + ex.Message); }
    }

    private void Display(ModelSettings settings)
    {
        _refreshing = true;
        try
        {
            ItemsSource = settings.ModelProfiles!.Where(profile => profile.IsConfigured)
                .OrderBy(profile => profile.ProviderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
            SelectedItem = settings.ModelProfiles!.FirstOrDefault(profile => profile.Key == settings.DefaultProfileKey);
            ToolTip = SelectedItem is ModelProfile model
                ? model.Label + " · 点击切换默认模型"
                : "请在模型设置中先配置模型";
        }
        finally { _refreshing = false; }
    }
}
