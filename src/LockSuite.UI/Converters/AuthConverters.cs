using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LockSuite.UI.Converters;

public class AuthStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Desbloqueado (Edición)" : "Bloqueado (Solo Lectura)";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class AuthColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new((Color)ColorConverter.ConvertFromString("#10B981"));
    private static readonly SolidColorBrush GrayBrush = new((Color)ColorConverter.ConvertFromString("#94A3B8"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? GreenBrush : GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class AuthButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Bloquear Tutor" : "Desbloquear Tutor";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
