using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FolderScope.Desktop;

internal static class ModelGrouping
{
    internal static List<ModelProfile> ShareKeys(List<ModelProfile> profiles, string defaultKey)
    {
        var keys = profiles.GroupBy(profile => profile.ProviderId).ToDictionary(group => group.Key,
            group => group.FirstOrDefault(profile => profile.Key == defaultKey && profile.ProtectedKey.Length > 0)?.ProtectedKey
                ?? group.FirstOrDefault(profile => profile.ProtectedKey.Length > 0)?.ProtectedKey ?? "");
        return profiles.Select(profile => profile with { ProtectedKey = keys[profile.ProviderId] }).ToList();
    }

    internal static (List<ModelProfile> Profiles, string DefaultKey) MigrateLegacy(List<ModelProfile> profiles, string defaultKey)
    {
        var presets = ModelCatalog.Presets();
        foreach (var legacy in profiles.Where(profile => profile.Key.StartsWith("legacy:", StringComparison.Ordinal)).ToArray())
        {
            if (!Uri.TryCreate(legacy.BaseUrl, UriKind.Absolute, out var endpoint) || endpoint.Host != "api.deepseek.com") continue;
            var model = legacy.Model is "deepseek-flash" or "deepseek-chat" ? "deepseek-v4-flash" : legacy.Model;
            var preset = presets.FirstOrDefault(profile => profile.ProviderId == "deepseek" && profile.Model == model);
            if (preset is null) continue;
            var normalized = legacy with { Key = preset.Key, ProviderId = preset.ProviderId, ProviderName = preset.ProviderName,
                DisplayName = preset.DisplayName, IsCustom = false };
            profiles.RemoveAll(profile => profile.Key == legacy.Key || profile.Key == preset.Key);
            profiles.Add(normalized);
            if (defaultKey == legacy.Key) defaultKey = preset.Key;
        }
        return (profiles, defaultKey);
    }

    internal static void Configure(ListBox list, Func<string> defaultKey)
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var expander = new FrameworkElementFactory(typeof(Expander));
        expander.SetValue(Expander.IsExpandedProperty, false);
        expander.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 5));
        expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding { Converter = new GroupHeader(defaultKey) });
        expander.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        list.GroupStyle.Add(new GroupStyle
        {
            ContainerStyle = new Style(typeof(GroupItem))
            {
                Setters = { new Setter(Control.TemplateProperty, new ControlTemplate(typeof(GroupItem)) { VisualTree = expander }) }
            }
        });
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var dot = new FrameworkElementFactory(typeof(TextBlock));
        dot.SetValue(TextBlock.ForegroundProperty, Brushes.SeaGreen);
        dot.SetValue(FrameworkElement.WidthProperty, 16d);
        dot.SetBinding(TextBlock.TextProperty, new Binding("Key") { Converter = new DefaultMarker(defaultKey) });
        row.AppendChild(dot);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding("DisplayName"));
        name.SetBinding(TextBlock.ForegroundProperty, new Binding("IsConfigured") { Converter = new ConfigurationColor() });
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        row.AppendChild(name);
        list.ItemTemplate = new DataTemplate { VisualTree = row };
    }

    internal static ListCollectionView View(List<ModelProfile> profiles)
    {
        var view = new ListCollectionView(profiles);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModelProfile.ProviderId)));
        return view;
    }

    private sealed class GroupHeader(Func<string> defaultKey) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not CollectionViewGroup group) return "";
            var profiles = group.Items.OfType<ModelProfile>().ToArray();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = profiles.Any(profile => profile.Key == defaultKey()) ? "●" : "",
                Foreground = Brushes.SeaGreen,
                Margin = new Thickness(0, 0, 7, 0) });
            row.Children.Add(new TextBlock { Text = $"{profiles.FirstOrDefault()?.ProviderName}  ({group.ItemCount})",
                FontWeight = FontWeights.SemiBold,
                Foreground = profiles.Any(profile => profile.IsConfigured) ? Brushes.Black : Brushes.Gray });
            return row;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class ConfigurationColor : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Brushes.Black : Brushes.Gray;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class DefaultMarker(Func<string> defaultKey) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string key && key == defaultKey() ? "●" : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
