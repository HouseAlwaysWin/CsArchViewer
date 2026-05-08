using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CsArchViewer.Avalonia.Views;

public partial class MetricsDashboardView : UserControl
{
    public MetricsDashboardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
