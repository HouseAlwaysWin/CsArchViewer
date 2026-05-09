using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Media;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.Controls;

public sealed partial class GraphCanvas : Control
{
    private static readonly Color[] EdgePalette =
    [
        Color.Parse("#60A5FA"),
        Color.Parse("#F59E0B"),
        Color.Parse("#34D399"),
        Color.Parse("#F472B6"),
        Color.Parse("#A78BFA"),
        Color.Parse("#22D3EE"),
        Color.Parse("#F87171"),
        Color.Parse("#FBBF24")
    ];

    public static readonly StyledProperty<IReadOnlyList<ArchitectureNode>> NodesProperty =
        AvaloniaProperty.Register<GraphCanvas, IReadOnlyList<ArchitectureNode>>(nameof(Nodes), []);

    public static readonly StyledProperty<IReadOnlyList<ArchitectureEdge>> EdgesProperty =
        AvaloniaProperty.Register<GraphCanvas, IReadOnlyList<ArchitectureEdge>>(nameof(Edges), []);

    public static readonly StyledProperty<ArchitectureNode?> SelectedNodeProperty =
        AvaloniaProperty.Register<GraphCanvas, ArchitectureNode?>(nameof(SelectedNode));

    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<GraphCanvas, int>(nameof(RenderVersion), 0);

    public static readonly StyledProperty<int> AutoFitVersionProperty =
        AvaloniaProperty.Register<GraphCanvas, int>(nameof(AutoFitVersion), 0);
    public static readonly StyledProperty<bool> ShowLineCountOnNodesProperty =
        AvaloniaProperty.Register<GraphCanvas, bool>(nameof(ShowLineCountOnNodes), false);

    private const double NodeWidth = 180;
    private const double NodeHeight = 72;

    private Point _panOffset = new(0, 0);
    private double _zoom = 1.0;
    private bool _isPanning;
    private ArchitectureNode? _dragNode;
    private ArchitectureEdge? _dragEdge;
    private EdgeSegmentOrientation _dragEdgeSegment = EdgeSegmentOrientation.Unknown;
    private Point _lastPointer;
    private readonly Dictionary<string, Vector> _edgeOffsets = new(StringComparer.OrdinalIgnoreCase);

    public event Action<ArchitectureNode>? NodeDoubleClicked;

    public IReadOnlyList<ArchitectureNode> Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public IReadOnlyList<ArchitectureEdge> Edges
    {
        get => GetValue(EdgesProperty);
        set => SetValue(EdgesProperty, value);
    }

    public ArchitectureNode? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public int RenderVersion
    {
        get => GetValue(RenderVersionProperty);
        set => SetValue(RenderVersionProperty, value);
    }

    public int AutoFitVersion
    {
        get => GetValue(AutoFitVersionProperty);
        set => SetValue(AutoFitVersionProperty, value);
    }

    public bool ShowLineCountOnNodes
    {
        get => GetValue(ShowLineCountOnNodesProperty);
        set => SetValue(ShowLineCountOnNodesProperty, value);
    }

    public GraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodesProperty || change.Property == EdgesProperty || change.Property == SelectedNodeProperty || change.Property == RenderVersionProperty || change.Property == ShowLineCountOnNodesProperty)
        {
            if (change.Property == NodesProperty || change.Property == EdgesProperty)
            {
                _edgeOffsets.Clear();
                _dragEdge = null;
                _dragEdgeSegment = EdgeSegmentOrientation.Unknown;
            }
            InvalidateVisual();
        }

        if (change.Property == AutoFitVersionProperty)
        {
            QueueFitToScreen();
        }
    }

    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        QueueFitToScreen();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, Bounds);

        var selectedFill = new SolidColorBrush(Color.Parse("#2563EB"));
        var libraryFill = new SolidColorBrush(Color.Parse("#1F2937"));
        var executableFill = new SolidColorBrush(Color.Parse("#065F46"));
        var folderFill = new SolidColorBrush(Color.Parse("#4C1D95"));
        var fileFill = new SolidColorBrush(Color.Parse("#334155"));
        var xamlFileFill = new SolidColorBrush(Color.Parse("#0EA5E9"));
        var razorFileFill = new SolidColorBrush(Color.Parse("#8B5CF6"));
        var cshtmlFileFill = new SolidColorBrush(Color.Parse("#F97316"));
        var solutionFill = new SolidColorBrush(Color.Parse("#7C2D12"));
        var packageFill = new SolidColorBrush(Color.Parse("#14B8A6"));
        var groupFill = new SolidColorBrush(Color.Parse("#0F4C5C"));
        var namespaceFill = new SolidColorBrush(Color.Parse("#0F766E"));
        var violationFill = new SolidColorBrush(Color.Parse("#B91C1C"));
        var typeFill = new SolidColorBrush(Color.Parse("#1D4ED8"));
        var interfaceFill = new SolidColorBrush(Color.Parse("#38BDF8"));
        var structFill = new SolidColorBrush(Color.Parse("#7C3AED"));
        var enumFill = new SolidColorBrush(Color.Parse("#BE185D"));
        var recordFill = new SolidColorBrush(Color.Parse("#10B981"));
        var highlightedPen = new Pen(new SolidColorBrush(Color.Parse("#F59E0B")), 2);
        var dependencyPathPen = new Pen(new SolidColorBrush(Color.Parse("#22D3EE")), 3);
        var textBrush = Brushes.White;

        var renderedEdges = new List<RenderedEdge>();
        foreach (var edge in Edges)
        {
            if (!IsEdgeVisible(edge))
            {
                continue;
            }

            var from = Nodes.FirstOrDefault(node => node.Id == edge.FromNodeId);
            var to = Nodes.FirstOrDefault(node => node.Id == edge.ToNodeId);
            if (from is null || to is null)
            {
                continue;
            }

            if (!IsNodeVisible(from) || !IsNodeVisible(to))
            {
                continue;
            }

            if (edge.Type == ArchitectureEdgeType.Contains)
            {
                var containsPath = BuildContainsPath(edge, from, to);
                renderedEdges.Add(new RenderedEdge(edge, containsPath, new Point(containsPath.End.X, containsPath.End.Y - 12)));
            }
            else
            {
                var referencePath = BuildReferencePath(edge, from, to);
                renderedEdges.Add(new RenderedEdge(edge, referencePath, new Point(referencePath.End.X - 12, referencePath.End.Y)));
            }
        }

        var colors = AssignEdgeColors(renderedEdges);
        foreach (var rendered in renderedEdges)
        {
            var isDependencyPathHit = rendered.Edge.Metadata.TryGetValue("IsDependencyPathHit", out var pathValue) &&
                                      string.Equals(pathValue, "true", StringComparison.OrdinalIgnoreCase);
            var edgeColor = isDependencyPathHit
                ? Color.Parse("#FDE047")
                : colors.TryGetValue(GetEdgeKey(rendered.Edge), out var color)
                ? color
                : Color.Parse("#6B7280");
            var pen = new Pen(new SolidColorBrush(edgeColor), isDependencyPathHit ? 3 : 1.5);

            var referencePath = rendered.Path;
            DrawPolyline(context, pen, referencePath.Points);
            DrawArrow(context, rendered.ArrowFrom, referencePath.End, edgeColor);

            if (!string.IsNullOrWhiteSpace(rendered.Edge.Label))
            {
                var mid = referencePath.LabelAnchor;
                var label = new FormattedText(
                    rendered.Edge.Label,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    10 * _zoom,
                    new SolidColorBrush(Color.Parse("#CBD5E1")));
                context.DrawText(label, mid + new Vector(4, -14));
            }
        }

        foreach (var node in Nodes)
        {
            if (!IsNodeVisible(node))
            {
                continue;
            }

            var topLeft = TransformPoint(new Point(node.X, node.Y));
            var overlayScale = node.Metadata.TryGetValue("OverlayScale", out var rawScale) &&
                               double.TryParse(rawScale, out var parsedScale)
                ? Math.Clamp(parsedScale, 0.7, 2.0)
                : 1.0;
            var scaledWidth = NodeWidth * _zoom * overlayScale;
            var scaledHeight = NodeHeight * _zoom * overlayScale;
            var rect = new Rect(topLeft, new Size(scaledWidth, scaledHeight));
            var outputType = node.Metadata.TryGetValue("OutputType", out var typeValue) ? typeValue : string.Empty;
            var isSearchHit = node.Metadata.TryGetValue("IsSearchHit", out var hitValue) &&
                              string.Equals(hitValue, "true", StringComparison.OrdinalIgnoreCase);
            var isDependencyPathHit = node.Metadata.TryGetValue("IsDependencyPathHit", out var pathValue) &&
                                      string.Equals(pathValue, "true", StringComparison.OrdinalIgnoreCase);
            var overlayBrush = node.Metadata.TryGetValue("OverlayColor", out var overlayColor) &&
                               !string.IsNullOrWhiteSpace(overlayColor)
                ? new SolidColorBrush(Color.Parse(overlayColor))
                : null;
            var overlayPen = overlayBrush is not null
                ? new Pen(overlayBrush, 2.5)
                : null;
            var baseFill = node.Type switch
            {
                ArchitectureNodeType.Group => groupFill,
                ArchitectureNodeType.Folder => folderFill,
                ArchitectureNodeType.File => ResolveFileFill(node, fileFill, xamlFileFill, razorFileFill, cshtmlFileFill),
                ArchitectureNodeType.Solution => solutionFill,
                ArchitectureNodeType.Package => packageFill,
                ArchitectureNodeType.Namespace => namespaceFill,
                ArchitectureNodeType.Violation => violationFill,
                ArchitectureNodeType.Type => typeFill,
                ArchitectureNodeType.Interface => interfaceFill,
                ArchitectureNodeType.Struct => structFill,
                ArchitectureNodeType.Enum => enumFill,
                ArchitectureNodeType.Record => recordFill,
                _ => string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase)
                    ? executableFill
                    : libraryFill
            };
            var fill = ReferenceEquals(node, SelectedNode) ? selectedFill : baseFill;

            context.FillRectangle(fill, rect, 8);
            if (overlayPen is not null)
            {
                var accentHeight = Math.Clamp(6 * _zoom, 4, 10);
                var accentRect = new Rect(
                    rect.X + (6 * _zoom),
                    rect.Y + (6 * _zoom),
                    Math.Max(16, rect.Width - (12 * _zoom)),
                    accentHeight);
                context.FillRectangle(overlayBrush!, accentRect, 3);
            }

            context.DrawRectangle(
                isDependencyPathHit ? dependencyPathPen : isSearchHit ? highlightedPen : overlayPen ?? new Pen(Brushes.Black, 1),
                rect,
                8);

            var displayName = BuildNodeDisplayName(node);
            var text = new FormattedText(
                displayName,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                14 * _zoom,
                textBrush);
            var horizontalPadding = 10 * _zoom;
            var verticalPadding = 10 * _zoom;
            var availableWidth = Math.Max(20, rect.Width - (horizontalPadding * 2));
            var availableHeight = Math.Max(20, rect.Height - (verticalPadding * 2));
            text.MaxTextWidth = availableWidth;
            text.MaxTextHeight = availableHeight;
            text.Trimming = TextTrimming.CharacterEllipsis;

            context.DrawText(text, rect.TopLeft + new Vector(horizontalPadding, verticalPadding));
        }
    }

}
