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
            if (!IsEdgeVisible(edge))
            {
                continue;
            }

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
        var (fromSide, toSide) = DetermineAnchorSides(from, to, preferVertical: true);
        var key = GetEdgeKey(edge);
        var offset = GetEdgeOffset(key) + GetAutoContainsOffset(edge);
        var p1 = GetAnchorPoint(from, fromSide);
        var p2 = GetAnchorPoint(to, toSide);
        return BuildOrthogonalPath(p1, fromSide, p2, toSide, offset);
    }

    private EdgePath BuildReferencePath(ArchitectureEdge edge, ArchitectureNode from, ArchitectureNode to)
    {
        var (fromSide, toSide) = DetermineAnchorSides(from, to, preferVertical: false);
        var key = GetEdgeKey(edge);
        var offset = GetEdgeOffset(key) + GetAutoReferenceOffset(edge, from, to);
        var p1 = GetAnchorPoint(from, fromSide);
        var p2 = GetAnchorPoint(to, toSide);
        return BuildOrthogonalPath(p1, fromSide, p2, toSide, offset);
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

    private static (NodeAnchorSide From, NodeAnchorSide To) DetermineAnchorSides(
        ArchitectureNode from,
        ArchitectureNode to,
        bool preferVertical)
    {
        var fromCenter = GetNodeCenter(from);
        var toCenter = GetNodeCenter(to);
        var dx = toCenter.X - fromCenter.X;
        var dy = toCenter.Y - fromCenter.Y;
        var verticalDominant = Math.Abs(dy) > Math.Abs(dx);
        var useVertical = preferVertical ? verticalDominant || Math.Abs(dy) >= Math.Abs(dx) : verticalDominant;

        if (useVertical)
        {
            return dy >= 0
                ? (NodeAnchorSide.Bottom, NodeAnchorSide.Top)
                : (NodeAnchorSide.Top, NodeAnchorSide.Bottom);
        }

        return dx >= 0
            ? (NodeAnchorSide.Right, NodeAnchorSide.Left)
            : (NodeAnchorSide.Left, NodeAnchorSide.Right);
    }

    private static Point GetAnchorPoint(ArchitectureNode node, NodeAnchorSide side)
    {
        var bounds = GetNodeBounds(node);
        var centerX = bounds.X + (bounds.Width / 2d);
        var centerY = bounds.Y + (bounds.Height / 2d);
        return side switch
        {
            NodeAnchorSide.Left => new Point(bounds.X, centerY),
            NodeAnchorSide.Right => new Point(bounds.Right, centerY),
            NodeAnchorSide.Top => new Point(centerX, bounds.Y),
            NodeAnchorSide.Bottom => new Point(centerX, bounds.Bottom),
            _ => new Point(bounds.Right, centerY)
        };
    }

    private EdgePath BuildOrthogonalPath(
        Point start,
        NodeAnchorSide startSide,
        Point end,
        NodeAnchorSide endSide,
        Vector offset)
    {
        var clampedOffset = ClampEdgeOffset(offset, start, end);
        var lead = 26d;
        var terminalGap = 12d;
        var startNormal = GetSideNormal(startSide);
        var endNormal = GetSideNormal(endSide);
        var horizontalRoute = IsHorizontalSide(startSide) && IsHorizontalSide(endSide);

        var rawPoints = new List<Point>(8);
        Point labelAnchor;

        if (horizontalRoute)
        {
            var startExit = new Point(start.X + (startNormal.X * lead), start.Y);
            var endEntry = new Point(end.X + (endNormal.X * lead), end.Y);
            var corridorStartX = ClampOutside(startExit.X + clampedOffset.X, start.X, startSide, terminalGap);
            var corridorEndX = ClampOutside(endEntry.X + clampedOffset.X, end.X, endSide, terminalGap);
            var midY = ((start.Y + end.Y) / 2d) + clampedOffset.Y;

            AppendPoint(rawPoints, start);
            AppendPoint(rawPoints, startExit);
            AppendPoint(rawPoints, new Point(corridorStartX, startExit.Y));
            AppendPoint(rawPoints, new Point(corridorStartX, midY));
            AppendPoint(rawPoints, new Point(corridorEndX, midY));
            AppendPoint(rawPoints, new Point(corridorEndX, endEntry.Y));
            AppendPoint(rawPoints, endEntry);
            AppendPoint(rawPoints, end);

            labelAnchor = TransformPoint(new Point((corridorStartX + corridorEndX) / 2d, midY));
        }
        else
        {
            var startExit = new Point(start.X, start.Y + (startNormal.Y * lead));
            var endEntry = new Point(end.X, end.Y + (endNormal.Y * lead));
            var corridorStartY = ClampOutside(startExit.Y + clampedOffset.Y, start.Y, startSide, terminalGap);
            var corridorEndY = ClampOutside(endEntry.Y + clampedOffset.Y, end.Y, endSide, terminalGap);
            var midX = ((start.X + end.X) / 2d) + clampedOffset.X;

            AppendPoint(rawPoints, start);
            AppendPoint(rawPoints, startExit);
            AppendPoint(rawPoints, new Point(startExit.X, corridorStartY));
            AppendPoint(rawPoints, new Point(midX, corridorStartY));
            AppendPoint(rawPoints, new Point(midX, corridorEndY));
            AppendPoint(rawPoints, new Point(endEntry.X, corridorEndY));
            AppendPoint(rawPoints, endEntry);
            AppendPoint(rawPoints, end);

            labelAnchor = TransformPoint(new Point(midX, (corridorStartY + corridorEndY) / 2d));
        }

        var points = rawPoints.Select(TransformPoint).ToArray();
        return new EdgePath(points, points[^1], labelAnchor);
    }

    private Vector GetAutoContainsOffset(ArchitectureEdge edge)
    {
        var siblings = Edges
            .Where(e => e.Type == ArchitectureEdgeType.Contains &&
                        string.Equals(e.FromNodeId, edge.FromNodeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.ToNodeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (siblings.Count <= 1)
        {
            return default;
        }

        var index = siblings.FindIndex(e =>
            string.Equals(e.FromNodeId, edge.FromNodeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ToNodeId, edge.ToNodeId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return default;
        }

        var lane = index - ((siblings.Count - 1) / 2d);
        return new Vector(lane * 18d, lane * 6d);
    }

    private Vector GetAutoReferenceOffset(ArchitectureEdge edge, ArchitectureNode from, ArchitectureNode to)
    {
        var outgoing = Edges
            .Where(e => e.Type != ArchitectureEdgeType.Contains &&
                        string.Equals(e.FromNodeId, edge.FromNodeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.ToNodeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var incoming = Edges
            .Where(e => e.Type != ArchitectureEdgeType.Contains &&
                        string.Equals(e.ToNodeId, edge.ToNodeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outgoingIndex = outgoing.FindIndex(e =>
            string.Equals(e.FromNodeId, edge.FromNodeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ToNodeId, edge.ToNodeId, StringComparison.OrdinalIgnoreCase));
        var incomingIndex = incoming.FindIndex(e =>
            string.Equals(e.FromNodeId, edge.FromNodeId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ToNodeId, edge.ToNodeId, StringComparison.OrdinalIgnoreCase));
        if (outgoingIndex < 0 || incomingIndex < 0)
        {
            return default;
        }

        var outgoingLane = outgoingIndex - ((outgoing.Count - 1) / 2d);
        var incomingLane = incomingIndex - ((incoming.Count - 1) / 2d);
        var lane = outgoingLane + incomingLane;
        var closeRangeFactor = Math.Abs(to.X - from.X) < 280d ? 1.2d : 0.55d;
        return new Vector(lane * 10d * closeRangeFactor, lane * 16d);
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

    private static bool IsHorizontalSide(NodeAnchorSide side)
    {
        return side is NodeAnchorSide.Left or NodeAnchorSide.Right;
    }

    private static Vector GetSideNormal(NodeAnchorSide side)
    {
        return side switch
        {
            NodeAnchorSide.Left => new Vector(-1, 0),
            NodeAnchorSide.Right => new Vector(1, 0),
            NodeAnchorSide.Top => new Vector(0, -1),
            NodeAnchorSide.Bottom => new Vector(0, 1),
            _ => default
        };
    }

    private static double ClampOutside(double value, double boundary, NodeAnchorSide side, double minDistance)
    {
        return side switch
        {
            NodeAnchorSide.Left => Math.Min(value, boundary - minDistance),
            NodeAnchorSide.Right => Math.Max(value, boundary + minDistance),
            NodeAnchorSide.Top => Math.Min(value, boundary - minDistance),
            NodeAnchorSide.Bottom => Math.Max(value, boundary + minDistance),
            _ => value
        };
    }

    private static void AppendPoint(List<Point> points, Point point)
    {
        if (points.Count == 0 || !IsSamePoint(points[^1], point, 0.0001))
        {
            points.Add(point);
        }
    }

    private readonly record struct EdgePath(IReadOnlyList<Point> Points, Point End, Point LabelAnchor);
    private readonly record struct RenderedEdge(ArchitectureEdge Edge, EdgePath Path, Point ArrowFrom);

    private enum EdgeSegmentOrientation
    {
        Unknown,
        Horizontal,
        Vertical
    }

    private enum NodeAnchorSide
    {
        Left,
        Right,
        Top,
        Bottom
    }
}
