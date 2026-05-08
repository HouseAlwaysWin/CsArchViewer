using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CsArchViewer.Diagnostics;

namespace CsArchViewer.Avalonia.Converters;

public sealed class DiagnosticSeverityBrushConverter : IValueConverter
{
    private static readonly IBrush InfoAccent = new SolidColorBrush(Color.Parse("#38BDF8"));
    private static readonly IBrush InfoBackground = new SolidColorBrush(Color.Parse("#082F49"));
    private static readonly IBrush InfoForeground = new SolidColorBrush(Color.Parse("#E0F2FE"));

    private static readonly IBrush WarningAccent = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush WarningBackground = new SolidColorBrush(Color.Parse("#451A03"));
    private static readonly IBrush WarningForeground = new SolidColorBrush(Color.Parse("#FEF3C7"));

    private static readonly IBrush ErrorAccent = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush ErrorBackground = new SolidColorBrush(Color.Parse("#450A0A"));
    private static readonly IBrush ErrorForeground = new SolidColorBrush(Color.Parse("#FEE2E2"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DiagnosticSeverity severity)
        {
            return Brushes.Transparent;
        }

        var role = parameter as string ?? "Accent";
        return role switch
        {
            "Background" => GetBackground(severity),
            "Foreground" => GetForeground(severity),
            _ => GetAccent(severity)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IBrush GetAccent(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Info => InfoAccent,
        DiagnosticSeverity.Warning => WarningAccent,
        DiagnosticSeverity.Error => ErrorAccent,
        _ => Brushes.Transparent
    };

    private static IBrush GetBackground(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Info => InfoBackground,
        DiagnosticSeverity.Warning => WarningBackground,
        DiagnosticSeverity.Error => ErrorBackground,
        _ => Brushes.Transparent
    };

    private static IBrush GetForeground(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Info => InfoForeground,
        DiagnosticSeverity.Warning => WarningForeground,
        DiagnosticSeverity.Error => ErrorForeground,
        _ => Brushes.White
    };
}
