using System.Windows;
using System.Windows.Markup;

namespace FolderScope.Desktop;

internal static class InterfaceTheme
{
    public static ResourceDictionary Create() => (ResourceDictionary)XamlReader.Parse("""
    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Style TargetType="Button">
        <Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#294250"/>
        <Setter Property="BorderBrush" Value="#DCE4E8"/><Setter Property="BorderThickness" Value="1"/>
        <Setter Property="FontSize" Value="12"/><Setter Property="MinHeight" Value="32"/>
        <Setter Property="Padding" Value="12,4"/><Setter Property="VerticalAlignment" Value="Center"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
          <Border x:Name="Box" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#69AA92"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#318369"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Box" Property="Opacity" Value="0.72"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Box" Property="Opacity" Value="0.38"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style x:Key="HistoryMenuButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="#E9F0F3"/>
        <Setter Property="BorderBrush" Value="#DCE4E8"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
          <Border x:Name="MenuSurface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
            BorderThickness="0,0,0,1" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="MenuSurface" Property="Background" Value="#DEEAEF"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="MenuSurface" Property="Background" Value="#DEEAEF"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="MenuSurface" Property="Background" Value="#D3E3E9"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="TextBox">
        <Setter Property="MinHeight" Value="32"/><Setter Property="FontSize" Value="12"/>
        <Setter Property="Padding" Value="10,2"/><Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#DCE4E8"/>
        <Setter Property="BorderThickness" Value="1"/><Setter Property="Foreground" Value="#263D49"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
          <Border x:Name="Box" CornerRadius="7" Background="{TemplateBinding Background}"
            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
            <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalScrollBarVisibility="{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}" HorizontalScrollBarVisibility="{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#5C9D83"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="PasswordBox">
        <Setter Property="MinHeight" Value="32"/><Setter Property="FontSize" Value="12"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Padding" Value="10,5"/><Setter Property="Background" Value="White"/>
        <Setter Property="BorderBrush" Value="#DCE4E8"/><Setter Property="BorderThickness" Value="1"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="PasswordBox">
          <Border x:Name="Box" CornerRadius="7" Background="{TemplateBinding Background}"
            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
            <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" VerticalScrollBarVisibility="{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}" HorizontalScrollBarVisibility="{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}"/>
          </Border><ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#5C9D83"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style x:Key="RoundedComboBox" TargetType="ComboBox">
        <Setter Property="MinHeight" Value="32"/><Setter Property="FontSize" Value="12"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Padding" Value="10,5"/><Setter Property="Background" Value="White"/>
        <Setter Property="Foreground" Value="#294250"/><Setter Property="BorderBrush" Value="#DCE4E8"/>
        <Setter Property="BorderThickness" Value="1"/><Setter Property="VerticalContentAlignment" Value="Center"/>
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
          <Grid>
            <ToggleButton Focusable="False" ClickMode="Press" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
              <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                <Border x:Name="Box" CornerRadius="7" Background="{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                  BorderBrush="{Binding BorderBrush, RelativeSource={RelativeSource AncestorType=ComboBox}}" BorderThickness="1">
                  <Path Data="M 1,2 L 5,6 L 9,2" Stroke="#607785" StrokeThickness="1.4" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0"/>
                </Border><ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#69AA92"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter IsHitTestVisible="False" Margin="10,4,30,4" VerticalAlignment="Center" ClipToBounds="True"
              Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
              ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"/>
            <Popup x:Name="PART_Popup" Placement="Bottom" AllowsTransparency="True" Focusable="False"
              IsOpen="{TemplateBinding IsDropDownOpen}" PopupAnimation="Fade">
              <Border Background="White" BorderBrush="#DCE4E8" BorderThickness="1" CornerRadius="7" Padding="4"
                MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="320">
                <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
              </Border>
            </Popup>
          </Grid><ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter Property="BorderBrush" Value="#318369"/></Trigger>
            <Trigger Property="IsDropDownOpen" Value="True"><Setter Property="BorderBrush" Value="#318369"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value></Setter>
      </Style>
      <Style TargetType="ComboBox" BasedOn="{StaticResource RoundedComboBox}"/>
      <Style TargetType="ComboBoxItem"><Setter Property="Padding" Value="8,6"/><Setter Property="FontSize" Value="12"/></Style>
      <Style TargetType="DataGrid">
        <Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#E0E7EB"/>
        <Setter Property="RowBackground" Value="White"/><Setter Property="AlternatingRowBackground" Value="#F8FAFB"/>
        <Setter Property="GridLinesVisibility" Value="Horizontal"/><Setter Property="HorizontalGridLinesBrush" Value="#EDF1F3"/>
        <Setter Property="HeadersVisibility" Value="Column"/><Setter Property="RowHeight" Value="30"/><Setter Property="FontSize" Value="12"/>
      </Style>
      <Style TargetType="DataGridColumnHeader">
        <Setter Property="Background" Value="#F1F5F7"/><Setter Property="Foreground" Value="#607785"/>
        <Setter Property="Padding" Value="10,7"/><Setter Property="FontSize" Value="11.5"/><Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="BorderThickness" Value="0,0,0,1"/><Setter Property="BorderBrush" Value="#E0E7EB"/>
      </Style>
      <Style TargetType="DataGridCell">
        <Setter Property="BorderThickness" Value="0"/><Setter Property="Padding" Value="8,0"/>
        <Setter Property="VerticalContentAlignment" Value="Center"/><Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Style.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#E7F2ED"/><Setter Property="Foreground" Value="#203E33"/></Trigger></Style.Triggers>
      </Style>
      <Style TargetType="TreeView"><Setter Property="Background" Value="White"/><Setter Property="BorderBrush" Value="#E0E7EB"/><Setter Property="Padding" Value="8"/><Setter Property="FontSize" Value="12"/></Style>
      <Style TargetType="TreeViewItem"><Setter Property="Padding" Value="4,5"/><Setter Property="Foreground" Value="#324B58"/></Style>
      <Style TargetType="Menu"><Setter Property="Background" Value="Transparent"/><Setter Property="Foreground" Value="#607785"/><Setter Property="FontSize" Value="12"/><Setter Property="Margin" Value="0,0,0,6"/></Style>
      <Style TargetType="MenuItem"><Setter Property="Padding" Value="8,5"/></Style>
    </ResourceDictionary>
    """);
}
