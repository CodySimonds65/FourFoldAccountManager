using System.Globalization;
using System.Windows.Data;

namespace FourFoldAccountManager.Desktop;

public sealed class ProfileInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string label && !string.IsNullOrWhiteSpace(label)
            ? StringInfo.GetNextTextElement(label.Trim()).ToUpper(culture)
            : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
