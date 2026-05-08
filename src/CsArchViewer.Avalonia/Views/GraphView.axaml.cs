using Avalonia.Controls;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.Views;

public partial class GraphView : UserControl
{
    public event Action<ArchitectureNode>? NodeDoubleClicked;

    public GraphView()
    {
        InitializeComponent();
        Canvas.NodeDoubleClicked += node => NodeDoubleClicked?.Invoke(node);
    }

    public void FitToScreen()
    {
        Canvas.FitToScreen();
    }

    public void ZoomToNode(ArchitectureNode? node)
    {
        if (node is null)
        {
            return;
        }

        Canvas.ZoomToNode(node);
    }
}
