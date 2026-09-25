using System.Text;
using System.Windows;
using System.Windows.Controls;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;
using ShortcutForge.Core.Model;
using ShortcutForge.Dsl;
using NameScope = ShortcutForge.Dsl.NameScope;

namespace ShortcutForge.App.Views;

/// <summary>
/// Choose a variable and, like tapping it in the Shortcuts app, the type to get it as and a
/// property or dictionary key. The result is a DSL variable expression, e.g. <c>data.as(Dictionary)["key"]</c>.
/// </summary>
public partial class VariablePickerWindow : Window
{
    private readonly NameScope _scope;
    private readonly object _noType = new TypeItem(null);

    public VariablePickerWindow(IEnumerable<string> variables, NameScope scope, string initialExpression)
    {
        InitializeComponent();
        _scope = scope;
        VariableBox.ItemsSource = variables.ToList();
        TypeBox.ItemsSource = new[] { _noType }.Concat(Aggrandizements.Types.Select(t => (object)new TypeItem(t))).ToList();
        TypeBox.SelectedItem = _noType;

        Prefill(initialExpression);

        VariableBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        VariableBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdatePreview);
        PropertyBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdatePreview()));
        PropertyBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdatePreview);
        KeyBox.TextChanged += (_, _) => UpdatePreview();
        Loaded += (_, _) => { UpdatePreview(); VariableBox.Focus(); };
    }

    /// <summary>The chosen expression, e.g. <c>name.as(Number)</c>.</summary>
    public string Result { get; private set; } = "";

    private sealed record TypeItem(Aggrandizements.ContentType? Type)
    {
        public override string ToString() => Type is null ? L.T("(eredeti típus)", "(original type)") : $"{Type.Label}  ·  {Type.Name}";
    }

    private void Prefill(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return;
        try
        {
            if (DslParser.ParseValue(expression, ParamKind.Variable, _scope) is not VariableValue { Variable: var v }) return;
            VariableBox.Text = DslPrinter.PrintValue(new VariableValue(v with { Extra = null }), ParamKind.Variable, _scope);
            if (!Aggrandizements.TryDescribe(v, out var itemClass, out var property, out var key)) return;
            if (itemClass is not null)
                TypeBox.SelectedItem = TypeBox.Items.OfType<TypeItem>().FirstOrDefault(t => t.Type?.ItemClass == itemClass) ?? _noType;
            PropertyBox.Text = property ?? "";
            KeyBox.Text = key ?? "";
        }
        catch (DslException)
        {
            VariableBox.Text = expression.Trim();
        }
    }

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var type = (TypeBox.SelectedItem as TypeItem)?.Type;
        var current = PropertyBox.Text;
        PropertyBox.ItemsSource = type?.Properties ?? (IEnumerable<string>)["Name"];
        PropertyBox.Text = current;
        UpdatePreview();
    }

    private string BuildExpression()
    {
        var sb = new StringBuilder(VariableBox.Text.Trim());
        if ((TypeBox.SelectedItem as TypeItem)?.Type is { } type) sb.Append(".as(").Append(type.Name).Append(')');
        if (!string.IsNullOrWhiteSpace(PropertyBox.Text)) sb.Append(".get(").Append(DslPrinter.Str(PropertyBox.Text.Trim())).Append(')');
        if (!string.IsNullOrWhiteSpace(KeyBox.Text)) sb.Append('[').Append(DslPrinter.Str(KeyBox.Text)).Append(']');
        return sb.ToString();
    }

    private bool Validate(string expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(VariableBox.Text))
        {
            error = L.T("Válassz változót.", "Choose a variable.");
            return false;
        }
        if (!string.IsNullOrWhiteSpace(PropertyBox.Text) && !string.IsNullOrWhiteSpace(KeyBox.Text))
        {
            error = L.T("Tulajdonság és szótár kulcs egyszerre nem adható meg.", "A property and a dictionary key cannot both be set.");
            return false;
        }
        try
        {
            if (DslParser.ParseValue(expression, ParamKind.Variable, _scope) is VariableValue) return true;
            error = L.T("Ez nem változó.", "This is not a variable.");
            return false;
        }
        catch (DslException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void UpdatePreview()
    {
        if (Preview is null) return;
        var expression = BuildExpression();
        Preview.Text = expression;
        var ok = Validate(expression, out var error);
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = ok;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var expression = BuildExpression();
        if (!Validate(expression, out _)) return;
        Result = expression;
        DialogResult = true;
    }
}
