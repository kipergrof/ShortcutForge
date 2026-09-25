using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Services;

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
/// XAML text in both languages: <c>Text="{l:T 'Mentés', 'Save'}"</c>. Updates live when the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string hu, string en) : MarkupExtension
{
    public string Hu { get; set; } = hu;
    public string En { get; set; } = en;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(LanguageSource.Language))
        {
            Source = LanguageSource.Instance,
            Mode = BindingMode.OneWay,
            Converter = new PairConverter(Hu, En),
        };
        return binding.ProvideValue(serviceProvider);
    }

    private sealed class PairConverter(string hu, string en) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value as string == L.English ? en : hu;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
