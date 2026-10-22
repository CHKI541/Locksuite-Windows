using System;
using System.Globalization;
using System.Windows.Data;
using LockSuite.Core;

namespace LockSuite.UI.Converters;

/// <summary>
/// Dominio bloqueado -> nota corta para el tutor en "Bloqueados Recientes"
/// (p. ej. "YouTube: bloqueado a propósito" o "Publicidad: no hace falta").
/// Devuelve "" si no hay nada que aclarar.
/// </summary>
public class BlockedDomainNoteConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Presets.ExplainBlocked(value as string) ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
