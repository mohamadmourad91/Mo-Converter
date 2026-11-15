using System.Windows.Markup;
using Mo.Engine;

namespace Mo.App.Views;

/// <summary>
/// XAML access to localized texts: <c>Text="{v:T settings.save}"</c>. Texts are read once when the view loads;
/// a language change takes effect after restarting the app.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Localizer.Current.T(Key);
}
