using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CsArchViewer.Avalonia.Views;

public partial class NodeDetailsSummaryView : UserControl
{
    public NodeDetailsSummaryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
