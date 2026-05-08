using System.Collections.ObjectModel;
using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed class GraphViewModel : ViewModelBase
{
    private ArchitectureNode? _selectedNode;
    private int _renderVersion;
    private int _autoFitVersion;
    private bool _showLineCountOnNodes;

    public ObservableCollection<ArchitectureNode> Nodes { get; } = [];
    public ObservableCollection<ArchitectureEdge> Edges { get; } = [];

    public ArchitectureNode? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    public int RenderVersion
    {
        get => _renderVersion;
        set => SetProperty(ref _renderVersion, value);
    }

    public int AutoFitVersion
    {
        get => _autoFitVersion;
        set => SetProperty(ref _autoFitVersion, value);
    }

    public bool ShowLineCountOnNodes
    {
        get => _showLineCountOnNodes;
        set
        {
            if (SetProperty(ref _showLineCountOnNodes, value))
            {
                Touch();
            }
        }
    }

    public void Touch()
    {
        RenderVersion++;
    }

    public void RequestAutoFit()
    {
        AutoFitVersion++;
    }
}
