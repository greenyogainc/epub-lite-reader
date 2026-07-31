using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Markup;

namespace EpubLiteReader;

/// <summary>Thin wrapper so code-behind and XAML share one lookup path into Strings.resx.</summary>
internal static class Strings
{
    private static readonly ResourceManager Manager =
        new("EpubLiteReader.Strings", typeof(Strings).Assembly);

    private static readonly HashSet<string> RightToLeftLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "ar" };

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static bool IsRightToLeft =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft &&
        RightToLeftLanguages.Contains(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static MessageBoxOptions MessageBoxOptions =>
        IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;

    public static void ApplyFlowDirection(Window window)
    {
        window.FlowDirection = IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
    }

    public static void ShowError(Window? owner, string message)
    {
        var caption = Get("AppTitle");
        if (owner is null)
            MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions);
        else
            MessageBox.Show(owner, message, caption, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions);
    }
}

[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
