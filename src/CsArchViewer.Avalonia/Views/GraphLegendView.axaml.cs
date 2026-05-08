using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CsArchViewer.Avalonia.Views;

public partial class GraphLegendView : UserControl
{
    public GraphLegendView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
