using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.Controls;

public sealed partial class GraphCanvas
{
    private static void DrawArrow(DrawingContext context, Point from, Point to, Color color)
    {
        var direction = to - from;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 0.01)
        {
            return;
        }

        var ux = direction.X / length;
        var uy = direction.Y / length;
        var arrowSize = 8;

        var tip = to;
        var left = new Point(
            tip.X - ux * arrowSize - uy * (arrowSize * 0.6),
            tip.Y - uy * arrowSize + ux * (arrowSize * 0.6));
        var right = new Point(
            tip.X - ux * arrowSize + uy * (arrowSize * 0.6),
            tip.Y - uy * arrowSize - ux * (arrowSize * 0.6));

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(tip, true);
        ctx.LineTo(left);
        ctx.LineTo(right);
        ctx.EndFigure(true);
        context.DrawGeometry(new SolidColorBrush(color), null, geometry);
    }

    private bool TryHitEdge(Point screenPoint, out ArchitectureEdge? hitEdge, out EdgeSegmentOrientation hitSegment)
    {
        const double tolerance = 8d;

        foreach (var edge in Edges.Reverse())
        {
            var from = Nodes.FirstOrDefault(node => node.Id == edge.FromNodeId);
            var to = Nodes.FirstOrDefault(node => node.Id == edge.ToNodeId);
            if (from is null || to is null || !IsNodeVisible(from) || !IsNodeVisible(to))
            {
                continue;
            }

            var path = edge.Type == ArchitectureEdgeType.Contains
                ? BuildContainsPath(edge, from, to)
                : BuildReferencePath(edge, from, to);
            if (!TryHitSegment(screenPoint, path.Points, tolerance, out hitSegment))
            {
                continue;
            }

            hitEdge = edge;
            return true;
        }

        hitEdge = null;
        hitSegment = EdgeSegmentOrientation.Unknown;
        return false;
    }

    private static void DrawPolyline(DrawingContext context, Pen pen, IReadOnlyList<Point> points)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            context.DrawLine(pen, points[i], points[i + 1]);
        }
    }

    private EdgePath BuildContainsPath(ArchitectureEdge edge, ArchitectureNode from, ArchitectureNode to)
    {
        var key = GetEdgeKey(edge);
        var offset = GetEdgeOffset(key);
        var p1 = new Point(from.X + NodeWidth / 2d, from.Y + NodeHeight);
        var p2 = new Point(to.X + NodeWidth / 2d, to.Y);
        var clampedOffset = ClampEdgeOffset(offset, p1, p2);
        var lead = 26d;
        var yTop = Math.Min(p1.Y + lead + clampedOffset.Y, p2.Y - 8);
        var yBottom = Math.Max(p2.Y - lead + clampedOffset.Y, p1.Y + 8);
        var midX = ((p1.X + p2.X) / 2d) + clampedOffset.X;

        var points = new[]
        {
            TransformPoint(p1),
            TransformPoint(new Point(p1.X, yTop)),
            TransformPoint(new Point(midX, yTop)),
            TransformPoint(new Point(midX, yBottom)),
            TransformPoint(new Point(p2.X, yBottom)),
            TransformPoint(p2)
        };
        var labelAnchor = TransformPoint(new Point(midX, (yTop + yBottom) / 2d));
        return new EdgePath(points, points[^1], labelAnchor);
    }

    private EdgePath BuildReferencePath(ArchitectureEdge edge, ArchitectureNode from, ArchitectureNode to)
    {
        var key = GetEdgeKey(edge);
        var offset = GetEdgeOffset(key);
        var p1 = new Point(from.X + NodeWidth, from.Y + NodeHeight / 2d);
        var p2 = new Point(to.X, to.Y + NodeHeight / 2d);
        var clampedOffset = ClampEdgeOffset(offset, p1, p2);
        var lead = 26d;
        var xLeft = Math.Min(p1.X + lead + clampedOffset.X, p2.X - 8);
        var xRight = Math.Max(p2.X - lead + clampedOffset.X, p1.X + 8);
        var midY = ((p1.Y + p2.Y) / 2d) + clampedOffset.Y;

        var points = new[]
        {
            TransformPoint(p1),
            TransformPoint(new Point(xLeft, p1.Y)),
            TransformPoint(new Point(xLeft, midY)),
            TransformPoint(new Point(xRight, midY)),
            TransformPoint(new Point(xRight, p2.Y)),
            TransformPoint(p2)
        };
        var labelAnchor = TransformPoint(new Point((xLeft + xRight) / 2d, midY));
        return new EdgePath(points, points[^1], labelAnchor);
    }

    private static string GetEdgeKey(ArchitectureEdge edge)
    {
        return $"{edge.Type}:{edge.FromNodeId}->{edge.ToNodeId}";
    }

    private Vector GetEdgeOffset(string key)
    {
        return _edgeOffsets.TryGetValue(key, out var value) ? value : default;
    }

    private static Vector ClampEdgeOffset(Vector offset, Point from, Point to)
    {
        var distanceX = Math.Abs(to.X - from.X);
        var distanceY = Math.Abs(to.Y - from.Y);
        var maxX = Math.Max(40d, distanceX * 0.75d);
        var maxY = Math.Max(40d, distanceY * 0.75d);
        return new Vector(
            Math.Clamp(offset.X, -maxX, maxX),
            Math.Clamp(offset.Y, -maxY, maxY));
    }

    private static bool TryHitSegment(Point point, IReadOnlyList<Point> points, double tolerance, out EdgeSegmentOrientation orientation)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            if (!IsPointNearSegment(point, a, b, tolerance))
            {
                continue;
            }

            orientation = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y)
                ? EdgeSegmentOrientation.Horizontal
                : EdgeSegmentOrientation.Vertical;
            return true;
        }

        orientation = EdgeSegmentOrientation.Unknown;
        return false;
    }

    private static bool IsPointNearSegment(Point p, Point a, Point b, double tolerance)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 0.0001)
        {
            var distSq = (p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y);
            return distSq <= tolerance * tolerance;
        }

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0d, 1d);
        var projX = a.X + t * dx;
        var projY = a.Y + t * dy;
        var distX = p.X - projX;
        var distY = p.Y - projY;
        return (distX * distX + distY * distY) <= tolerance * tolerance;
    }

    private static Dictionary<string, Color> AssignEdgeColors(IReadOnlyList<RenderedEdge> edges)
    {
        var conflicts = BuildConflictMap(edges);
        var result = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < edges.Count; i++)
        {
            var key = GetEdgeKey(edges[i].Edge);
            var usedColors = new HashSet<Color>();
            if (conflicts.TryGetValue(i, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    var neighborKey = GetEdgeKey(edges[neighbor].Edge);
                    if (result.TryGetValue(neighborKey, out var neighborColor))
                    {
                        usedColors.Add(neighborColor);
                    }
                }
            }

            var selected = EdgePalette.FirstOrDefault(c => !usedColors.Contains(c));
            if (selected == default)
            {
                selected = EdgePalette[i % EdgePalette.Length];
            }

            result[key] = selected;
        }

        return result;
    }

    private static Dictionary<int, HashSet<int>> BuildConflictMap(IReadOnlyList<RenderedEdge> edges)
    {
        var map = new Dictionary<int, HashSet<int>>();
        for (var i = 0; i < edges.Count; i++)
        {
            map[i] = [];
        }

        for (var i = 0; i < edges.Count; i++)
        {
            for (var j = i + 1; j < edges.Count; j++)
            {
                if (!PathsConflict(edges[i].Path.Points, edges[j].Path.Points))
                {
                    continue;
                }

                map[i].Add(j);
                map[j].Add(i);
            }
        }

        return map;
    }

    private static bool PathsConflict(IReadOnlyList<Point> first, IReadOnlyList<Point> second)
    {
        for (var i = 0; i < first.Count - 1; i++)
        {
            for (var j = 0; j < second.Count - 1; j++)
            {
                if (SegmentsConflict(first[i], first[i + 1], second[j], second[j + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsConflict(Point a1, Point a2, Point b1, Point b2)
    {
        const double epsilon = 0.0001;
        var aHorizontal = Math.Abs(a1.Y - a2.Y) < epsilon;
        var aVertical = Math.Abs(a1.X - a2.X) < epsilon;
        var bHorizontal = Math.Abs(b1.Y - b2.Y) < epsilon;
        var bVertical = Math.Abs(b1.X - b2.X) < epsilon;

        if (aHorizontal && bHorizontal && Math.Abs(a1.Y - b1.Y) < epsilon)
        {
            return RangesOverlap(a1.X, a2.X, b1.X, b2.X, epsilon);
        }

        if (aVertical && bVertical && Math.Abs(a1.X - b1.X) < epsilon)
        {
            return RangesOverlap(a1.Y, a2.Y, b1.Y, b2.Y, epsilon);
        }

        if ((aHorizontal && bVertical) || (aVertical && bHorizontal))
        {
            var horizontalStart = aHorizontal ? a1 : b1;
            var horizontalEnd = aHorizontal ? a2 : b2;
            var verticalStart = aVertical ? a1 : b1;
            var verticalEnd = aVertical ? a2 : b2;

            var cross = new Point(verticalStart.X, horizontalStart.Y);
            var withinHorizontal = IsBetween(cross.X, horizontalStart.X, horizontalEnd.X, epsilon);
            var withinVertical = IsBetween(cross.Y, verticalStart.Y, verticalEnd.Y, epsilon);
            if (!withinHorizontal || !withinVertical)
            {
                return false;
            }

            var isEndpointForA = IsSamePoint(cross, a1, epsilon) || IsSamePoint(cross, a2, epsilon);
            var isEndpointForB = IsSamePoint(cross, b1, epsilon) || IsSamePoint(cross, b2, epsilon);
            return !(isEndpointForA && isEndpointForB);
        }

        return false;
    }

    private static bool RangesOverlap(double x1, double x2, double y1, double y2, double epsilon)
    {
        var minA = Math.Min(x1, x2);
        var maxA = Math.Max(x1, x2);
        var minB = Math.Min(y1, y2);
        var maxB = Math.Max(y1, y2);
        return Math.Min(maxA, maxB) - Math.Max(minA, minB) > epsilon;
    }

    private static bool IsBetween(double value, double start, double end, double epsilon)
    {
        var min = Math.Min(start, end) - epsilon;
        var max = Math.Max(start, end) + epsilon;
        return value >= min && value <= max;
    }

    private static bool IsSamePoint(Point left, Point right, double epsilon)
    {
        return Math.Abs(left.X - right.X) < epsilon && Math.Abs(left.Y - right.Y) < epsilon;
    }

    private readonly record struct EdgePath(IReadOnlyList<Point> Points, Point End, Point LabelAnchor);
    private readonly record struct RenderedEdge(ArchitectureEdge Edge, EdgePath Path, Point ArrowFrom);

    private enum EdgeSegmentOrientation
    {
        Unknown,
        Horizontal,
        Vertical
    }
}
