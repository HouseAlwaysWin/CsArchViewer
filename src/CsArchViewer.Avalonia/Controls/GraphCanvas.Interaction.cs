using System;
using Avalonia;
using Avalonia.Input;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.Controls;

public sealed partial class GraphCanvas
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetPosition(this);
        _lastPointer = point;
        var pointerPoint = e.GetCurrentPoint(this);
        var properties = pointerPoint.Properties;
        var graphPoint = InverseTransformPoint(point);
        var node = HitTestNode(graphPoint);
        var hasHitEdge = TryHitEdge(point, out var hitEdge, out var hitSegment);

        if (properties.IsLeftButtonPressed)
        {
            if (node is not null)
            {
                _dragNode = node;
                _dragEdge = null;
                _isPanning = false;
                SelectedNode = node;
            }
            else if (hasHitEdge && hitEdge is not null)
            {
                _dragEdge = hitEdge;
                _dragEdgeSegment = hitSegment;
                _dragNode = null;
                _isPanning = false;
            }
            else
            {
                _dragNode = null;
                _dragEdge = null;
                _dragEdgeSegment = EdgeSegmentOrientation.Unknown;
                SelectedNode = null;
            }
        }
        else if (properties.IsRightButtonPressed || properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _dragNode = null;
            _dragEdge = null;
            _dragEdgeSegment = EdgeSegmentOrientation.Unknown;
        }

        if (e.ClickCount == 2)
        {
            if (node is not null)
            {
                NodeDoubleClicked?.Invoke(node);
            }
            else if (hasHitEdge && hitEdge is not null)
            {
                _edgeOffsets.Remove(GetEdgeKey(hitEdge));
            }
            else
            {
                FitToScreen();
            }
        }

        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var delta = point - _lastPointer;
        _lastPointer = point;
        var properties = e.GetCurrentPoint(this).Properties;
        var isAnyButtonPressed = properties.IsLeftButtonPressed || properties.IsRightButtonPressed || properties.IsMiddleButtonPressed;

        if (_dragNode is not null && properties.IsLeftButtonPressed)
        {
            _dragNode.X += delta.X / _zoom;
            _dragNode.Y += delta.Y / _zoom;
            InvalidateVisual();
        }
        else if (_dragEdge is not null && properties.IsLeftButtonPressed)
        {
            var key = GetEdgeKey(_dragEdge);
            var currentOffset = GetEdgeOffset(key);
            var nextOffset = currentOffset;
            if (_dragEdgeSegment == EdgeSegmentOrientation.Horizontal || _dragEdgeSegment == EdgeSegmentOrientation.Unknown)
            {
                nextOffset = new Vector(nextOffset.X, currentOffset.Y + (delta.Y / _zoom));
            }

            if (_dragEdgeSegment == EdgeSegmentOrientation.Vertical || _dragEdgeSegment == EdgeSegmentOrientation.Unknown)
            {
                nextOffset = new Vector(currentOffset.X + (delta.X / _zoom), nextOffset.Y);
            }

            _edgeOffsets[key] = nextOffset;
            InvalidateVisual();
        }
        else if (_isPanning && isAnyButtonPressed)
        {
            _panOffset += delta;
            InvalidateVisual();
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _isPanning = false;
        _dragNode = null;
        _dragEdge = null;
        _dragEdgeSegment = EdgeSegmentOrientation.Unknown;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var pointer = e.GetPosition(this);
        if ((e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
        {
            const double panStep = 60d;
            _panOffset += new Point(e.Delta.Y * panStep, 0);
            InvalidateVisual();
            e.Handled = true;
            base.OnPointerWheelChanged(e);
            return;
        }

        var graphBeforeZoom = InverseTransformPoint(pointer);
        var zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
        var nextZoom = Math.Clamp(_zoom * zoomFactor, 0.3, 2.5);
        if (Math.Abs(nextZoom - _zoom) < 0.0001)
        {
            return;
        }

        _zoom = nextZoom;
        _panOffset = pointer - new Point(graphBeforeZoom.X * _zoom, graphBeforeZoom.Y * _zoom);
        InvalidateVisual();
        e.Handled = true;
        base.OnPointerWheelChanged(e);
    }
}
