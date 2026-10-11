using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VDownloader.Controls;

/// <summary>Windows 11 设置页风格的卡片：左侧图标 + 标题/说明，右侧放任意控件。</summary>
public sealed class SettingsCard : ContentControl
{
    public SettingsCard() => DefaultStyleKey = typeof(SettingsCard);

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata("", OnDescriptionChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    private TextBlock? _desc;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _desc = GetTemplateChild("PART_Description") as TextBlock;
        UpdateDescription();
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SettingsCard)d).UpdateDescription();

    private void UpdateDescription()
    {
        if (_desc is not null)
            _desc.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
    }
}
