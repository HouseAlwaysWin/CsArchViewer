using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.Controls;

public sealed partial class GraphCanvas
{
    public void FitToScreen()
    {
        var visibleNodes = Nodes.Where(IsNodeVisible).ToList();
        if (visibleNodes.Count == 0)
        {
            return;
        }

        var minX = visibleNodes.Min(n => n.X);
        var minY = visibleNodes.Min(n => n.Y);
        var maxX = visibleNodes.Max(n => n.X + NodeWidth);
        var maxY = visibleNodes.Max(n => n.Y + NodeHeight);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);

        var scaleX = Bounds.Width / width * 0.85;
        var scaleY = Bounds.Height / height * 0.85;
        _zoom = Math.Clamp(Math.Min(scaleX, scaleY), 0.3, 2.5);

        var contentCenter = new Point((minX + maxX) / 2d, (minY + maxY) / 2d);
        var viewportCenter = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        _panOffset = viewportCenter - new Point(contentCenter.X * _zoom, contentCenter.Y * _zoom);

        InvalidateVisual();
    }

    public void ZoomToNode(ArchitectureNode node)
    {
        if (!Nodes.Contains(node) || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        _zoom = 1.45;
        var nodeCenter = new Point(node.X + (NodeWidth / 2d), node.Y + (NodeHeight / 2d));
        var viewportCenter = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        _panOffset = viewportCenter - new Point(nodeCenter.X * _zoom, nodeCenter.Y * _zoom);
        InvalidateVisual();
    }

    private void QueueFitToScreen()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (VisualRoot is null || Bounds.Width <= 1 || Bounds.Height <= 1)
            {
                return;
            }

            FitToScreen();
        }, DispatcherPriority.Background);
    }

    private ArchitectureNode? HitTestNode(Point graphPoint)
    {
        return Nodes.LastOrDefault(node =>
            IsNodeVisible(node) &&
            graphPoint.X >= node.X &&
            graphPoint.X <= node.X + NodeWidth &&
            graphPoint.Y >= node.Y &&
            graphPoint.Y <= node.Y + NodeHeight);
    }

    private static bool IsNodeVisible(ArchitectureNode node)
    {
        if (node.Metadata.TryGetValue("IsTypeVisible", out var value) &&
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private string BuildNodeDisplayName(ArchitectureNode node)
    {
        if (!ShowLineCountOnNodes || node.Type != ArchitectureNodeType.File)
        {
            return node.Name;
        }

        if (!node.Metadata.TryGetValue("LineCount", out var rawLineCount) || string.IsNullOrWhiteSpace(rawLineCount))
        {
            return node.Name;
        }

        return $"{node.Name}\n({rawLineCount} lines)";
    }

    private static IBrush ResolveFileFill(
        ArchitectureNode node,
        IBrush defaultFileFill,
        IBrush xamlFileFill,
        IBrush razorFileFill,
        IBrush cshtmlFileFill)
    {
        var extension = GetFileExtension(node);
        return extension switch
        {
            ".axaml" => xamlFileFill,
            ".xaml" => xamlFileFill,
            ".razor" => razorFileFill,
            ".cshtml" => cshtmlFileFill,
            _ => defaultFileFill
        };
    }

    private static string GetFileExtension(ArchitectureNode node)
    {
        if (node.Metadata.TryGetValue("Extension", out var metadataExtension) &&
            !string.IsNullOrWhiteSpace(metadataExtension))
        {
            var normalized = metadataExtension.Trim();
            return normalized.StartsWith(".", StringComparison.Ordinal)
                ? normalized.ToLowerInvariant()
                : $".{normalized.ToLowerInvariant()}";
        }

        var pathExtension = Path.GetExtension(node.FullPath ?? string.Empty);
        return string.IsNullOrWhiteSpace(pathExtension)
            ? string.Empty
            : pathExtension.ToLowerInvariant();
    }

    private Point TransformPoint(Point point)
    {
        return new Point(point.X * _zoom + _panOffset.X, point.Y * _zoom + _panOffset.Y);
    }

    private Point InverseTransformPoint(Point point)
    {
        return new Point((point.X - _panOffset.X) / _zoom, (point.Y - _panOffset.Y) / _zoom);
    }
}
