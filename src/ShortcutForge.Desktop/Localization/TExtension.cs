using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Desktop.Localization;

/// <summary>Bindable wrapper around <see cref="L.Language"/>; every <see cref="TExtension"/> binds to it.</summary>
public sealed class LanguageSource : INotifyPropertyChanged
{
    public static LanguageSource Instance { get; } = new();

    private LanguageSource()
    {
        L.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    public string Language => L.Language;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// XAML text in both languages, like in the Windows app: <c>Text="{l:T 'Mentés', 'Save'}"</c>.
/// Updates live when the language changes.
/// </summary>
public sealed class TExtension(string hu, string en) : MarkupExtension
{
    public string Hu { get; set; } = hu;
    public string En { get; set; } = en;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var hu = Hu;
        var en = En;
        return new Binding(nameof(LanguageSource.Language))
        {
            Source = LanguageSource.Instance,
            Mode = BindingMode.OneWay,
            Converter = new FuncValueConverter<string?, string>(language => language == L.English ? en : hu),
        };
    }
}
