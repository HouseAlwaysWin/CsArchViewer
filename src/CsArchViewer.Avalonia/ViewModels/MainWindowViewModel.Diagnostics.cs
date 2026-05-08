using System;
using System.Collections.Generic;
using System.Linq;
using CsArchViewer.Diagnostics;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void SetDiagnostics(IEnumerable<ArchitectureDiagnostic> diagnostics)
    {
        var uniqueDiagnostics = diagnostics
            .DistinctBy(d => $"{d.Severity}|{d.Type}|{d.Source}|{d.Target}|{d.Message}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        _allDiagnostics = uniqueDiagnostics;

        Diagnostics.Clear();
        foreach (var diagnostic in uniqueDiagnostics)
        {
            Diagnostics.Add(diagnostic);
        }

        ApplyDiagnosticsFilters();
    }

    private void ApplyDiagnosticsFilters()
    {
        var filtered = SelectedDiagnosticsSeverityFilter == "All"
            ? _allDiagnostics
            : _allDiagnostics.Where(diagnostic =>
                string.Equals(diagnostic.Severity.ToString(), SelectedDiagnosticsSeverityFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        FilteredDiagnostics.Clear();
        foreach (var diagnostic in filtered)
        {
            FilteredDiagnostics.Add(diagnostic);
        }
    }
}
