using Avalonia;
using Avalonia.Media;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.Controls;

/// <summary>
/// Shared node sizing for layout overlap resolution and canvas hit-testing so both use the same logical bounds.
/// </summary>
public static class GraphNodeLayoutMetrics
{
    public const double MinNodeWidth = 180;
    public const double MaxNodeWidth = 420;
    public const double NodeHeight = 72;

    public static double GetOverlayScale(ArchitectureNode node)
    {
        return node.Metadata.TryGetValue("OverlayScale", out var rawScale) &&
               double.TryParse(rawScale, out var parsedScale)
            ? Math.Clamp(parsedScale, 0.7, 2.0)
            : 1.0;
    }

    public static double MeasureBaseWidth(ArchitectureNode node)
    {
        var text = new FormattedText(
            node.Name ?? string.Empty,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            Brushes.White);
        var measuredWidth = text.Width + 26d;
        return Math.Clamp(measuredWidth, MinNodeWidth, MaxNodeWidth);
    }

    public static Size MeasureLogicalSize(ArchitectureNode node)
    {
        var scale = GetOverlayScale(node);
        return new Size(MeasureBaseWidth(node) * scale, NodeHeight * scale);
    }
}
