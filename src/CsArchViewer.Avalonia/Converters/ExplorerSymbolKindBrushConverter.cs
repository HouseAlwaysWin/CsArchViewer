using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CsArchViewer.DotNet.SymbolExplorer.Models;

namespace CsArchViewer.Avalonia.Converters;

public sealed class ExplorerSymbolKindBrushConverter : IValueConverter
{
    private static readonly IBrush TypeBrush = new SolidColorBrush(Color.Parse("#60A5FA"));
    private static readonly IBrush InterfaceBrush = new SolidColorBrush(Color.Parse("#34D399"));
    private static readonly IBrush MethodBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush PropertyBrush = new SolidColorBrush(Color.Parse("#A78BFA"));
    private static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#F472B6"));
    private static readonly IBrush EventBrush = new SolidColorBrush(Color.Parse("#22D3EE"));
    private static readonly IBrush NamespaceBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ExplorerSymbolKind kind)
        {
            return DefaultBrush;
        }

        return kind switch
        {
            ExplorerSymbolKind.Class or ExplorerSymbolKind.Record or ExplorerSymbolKind.Struct or ExplorerSymbolKind.Enum or ExplorerSymbolKind.Delegate => TypeBrush,
            ExplorerSymbolKind.Interface => InterfaceBrush,
            ExplorerSymbolKind.Method => MethodBrush,
            ExplorerSymbolKind.Property => PropertyBrush,
            ExplorerSymbolKind.Field => FieldBrush,
            ExplorerSymbolKind.Event => EventBrush,
            ExplorerSymbolKind.Namespace => NamespaceBrush,
            _ => DefaultBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
