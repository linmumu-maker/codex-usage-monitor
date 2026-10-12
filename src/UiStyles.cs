using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace CodexUsageMonitor
{
    internal static class UiStyles
    {
        internal static readonly FontFamily Font = new FontFamily("Segoe UI, Microsoft YaHei UI");
        internal static ResourceDictionary Resources(Palette colors)
        {
            var resources = (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style x:Key='KeyboardFocus' TargetType='Control'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Control'>
   <Border CornerRadius='7' BorderThickness='1' BorderBrush='{DynamicResource Focus}' Margin='-3'/>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='Button'>
  <Setter Property='Height' Value='34'/><Setter Property='FontSize' Value='13'/>
  <Setter Property='Background' Value='{DynamicResource Card}'/><Setter Property='Foreground' Value='{DynamicResource Foreground}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource Border}'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='12,0'/><Setter Property='HorizontalContentAlignment' Value='Center'/>
  <Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='FocusVisualStyle' Value='{StaticResource KeyboardFocus}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
    <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
   </Border>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers>
   <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource Hover}'/><Setter Property='BorderBrush' Value='{DynamicResource HoverBorder}'/></Trigger>
   <Trigger Property='IsPressed' Value='True'><Setter Property='Background' Value='{DynamicResource Pressed}'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
  </Style.Triggers>
 </Style>
 <Style x:Key='PrimaryButton' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
  <Setter Property='Background' Value='{DynamicResource Foreground}'/><Setter Property='Foreground' Value='{DynamicResource Card}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource Foreground}'/><Setter Property='FontWeight' Value='SemiBold'/>
  <Style.Triggers>
   <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource PrimaryHover}'/><Setter Property='BorderBrush' Value='{DynamicResource PrimaryHover}'/></Trigger>
   <Trigger Property='IsPressed' Value='True'><Setter Property='Background' Value='{DynamicResource PrimaryPressed}'/></Trigger>
  </Style.Triggers>
 </Style>
 <Style x:Key='RefreshButton' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
  <Setter Property='Height' Value='28'/><Setter Property='Padding' Value='0'/><Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='{DynamicResource Muted}'/>
  <Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/>
  <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
 </Style>
 <Style TargetType='TextBox'>
  <Setter Property='Height' Value='34'/><Setter Property='FontSize' Value='13'/>
  <Setter Property='Background' Value='{DynamicResource Input}'/><Setter Property='Foreground' Value='{DynamicResource Foreground}'/>
  <Setter Property='CaretBrush' Value='{DynamicResource Foreground}'/><Setter Property='SelectionBrush' Value='{DynamicResource Focus}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource Border}'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='10,0'/><Setter Property='VerticalContentAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
   <Border x:Name='Frame' CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
    <ScrollViewer x:Name='PART_ContentHost' Focusable='False' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource HoverBorder}'/></Trigger>
    <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource Focus}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBoxItem'>
  <Setter Property='Foreground' Value='{DynamicResource Foreground}'/><Setter Property='Padding' Value='10,6'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
   <Border x:Name='Item' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}'>
    <ContentPresenter/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter TargetName='Item' Property='Background' Value='{DynamicResource Pressed}'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBox'>
  <Setter Property='Height' Value='34'/><Setter Property='FontSize' Value='13'/>
  <Setter Property='Background' Value='{DynamicResource Input}'/><Setter Property='Foreground' Value='{DynamicResource Foreground}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource Border}'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='FocusVisualStyle' Value='{StaticResource KeyboardFocus}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
   <Grid>
    <Border x:Name='Frame' CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/>
    <ToggleButton Focusable='False' Background='Transparent' IsChecked='{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}'>
     <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'>
      <Border Background='Transparent'><TextBlock Text='&#xE70D;' FontFamily='Segoe MDL2 Assets' FontSize='10' Foreground='{DynamicResource Muted}' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/></Border>
     </ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Margin='10,0,34,0' VerticalAlignment='Center' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding ItemTemplate}' ContentStringFormat='{TemplateBinding SelectionBoxItemStringFormat}'/>
    <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False'>
     <Border Background='{DynamicResource Card}' BorderBrush='{DynamicResource Border}' BorderThickness='1' CornerRadius='6' Padding='4' MinWidth='{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}'>
      <ScrollViewer CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
     </Border>
    </Popup>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource HoverBorder}'/></Trigger>
    <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource Focus}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='CheckBox'>
  <Setter Property='FontSize' Value='13'/><Setter Property='Foreground' Value='{DynamicResource Foreground}'/>
  <Setter Property='FocusVisualStyle' Value='{StaticResource KeyboardFocus}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
   <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
    <Border x:Name='Box' Width='16' Height='16' CornerRadius='4' Background='{DynamicResource Input}' BorderBrush='{DynamicResource Border}' BorderThickness='1' VerticalAlignment='Center'>
     <TextBlock x:Name='Mark' Text='&#xE73E;' FontFamily='Segoe MDL2 Assets' FontSize='10' Foreground='White' Visibility='Collapsed' HorizontalAlignment='Center' VerticalAlignment='Center'/>
    </Border>
    <ContentPresenter Grid.Column='1' Margin='8,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{DynamicResource Focus}'/><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Focus}'/><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource HoverBorder}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>");
            resources["Card"] = colors.Background;
            resources["Window"] = colors.Window;
            resources["Foreground"] = colors.Foreground;
            resources["Muted"] = colors.Muted;
            resources["Border"] = colors.Border;
            resources["Input"] = colors.Input;
            resources["Hover"] = colors.Hover;
            resources["HoverBorder"] = colors.HoverBorder;
            resources["Pressed"] = colors.Pressed;
            resources["Focus"] = colors.Focus;
            resources["PrimaryHover"] = colors.PrimaryHover;
            resources["PrimaryPressed"] = colors.PrimaryPressed;
            return resources;
        }
        internal static void Apply(Window window, Palette colors)
        {
            window.Resources = Resources(colors);
            window.Background = colors.Window;
            window.Foreground = colors.Foreground;
            window.FontFamily = Font;
            window.FontSize = 13;
            window.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        }
        internal static TextBlock Text(string value, Brush color, double size)
        {
            return new TextBlock { Text = value, Foreground = color, FontFamily = Font, FontSize = size,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }
        internal static Border Card(Palette colors, UIElement content)
        {
            return new Border { Background = colors.Background, BorderBrush = colors.Border,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Child = content };
        }
    }
}
