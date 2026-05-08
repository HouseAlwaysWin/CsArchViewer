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
}
