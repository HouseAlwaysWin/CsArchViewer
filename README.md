# CsArchViewer

CsArchViewer is a lightweight .NET architecture explorer built with Avalonia.

## What This Project Does

- Scans a folder containing `.sln`/`.csproj` files and builds architecture graphs.
- Visualizes dependencies and structure from different perspectives:
  - Project, package, folder, file, namespace, type, and matrix views.
- Runs diagnostics (for example circular dependencies and rule violations).
- Collects metrics (LOC, coupling, health warnings) and shows them in a dashboard.
- Provides a Symbol Explorer (search symbols, find references, go to definition, inspect type/method metadata).
- Supports graph export (Mermaid / JSON / DOT) and metrics export (JSON / CSV / Markdown).
- Uses incremental analysis and file watching to keep results updated.

## Solution Layout

```text
src/
  CsArchViewer.Core/
  CsArchViewer.DotNet/
  CsArchViewer.DotNet.Roslyn/
  CsArchViewer.DotNet.SymbolAnalysis/
  CsArchViewer.DotNet.SymbolExplorer/
  CsArchViewer.Export/
  CsArchViewer.Analysis/
  CsArchViewer.Diagnostics/
  CsArchViewer.Metrics/
  CsArchViewer.Avalonia/
```

- `CsArchViewer.Core`: graph/project models and analyzer interface.
- `CsArchViewer.DotNet`: `.sln`/`.csproj` scanner, file scanner, graph generation.
- `CsArchViewer.DotNet.Roslyn`: Roslyn namespace analyzer, rule engine, cycle detector.
- `CsArchViewer.DotNet.SymbolAnalysis`: Roslyn semantic type/file analyzers and matrix builder.
- `CsArchViewer.DotNet.SymbolExplorer`: symbol index, search, find references, method/type lightweight analyzers, navigation targets (Roslyn only).
- `CsArchViewer.Export`: Mermaid / JSON / DOT exporters.
- `CsArchViewer.Analysis`: incremental engine, cache, scheduler, file tracker, explorer/search services.
- `CsArchViewer.Diagnostics`: architecture diagnostic analyzers and severity model.
- `CsArchViewer.Metrics`: Roslyn-based LOC analyzers, aggregated metrics and health warnings.
- `CsArchViewer.Avalonia`: UI, graph canvas, interaction, details pane.

## How To Build

```powershell
dotnet build .\CsArchViewer.sln
```

## How To Run

```powershell
dotnet run --project .\src\CsArchViewer.Avalonia\CsArchViewer.Avalonia.csproj
```

## How To Use

1. Launch app.
2. Click **Open Folder**.
3. Choose a folder containing a .NET solution.
4. View:
   - left: **Nodes** list or **Symbol Explorer** search (tabs)
   - center: graph view
   - right: **Architecture** node details or **Symbol Details** (references / type members / method metadata tabs)
   - bottom: **Diagnostics / Top Files** or **Symbol References** tab
5. After analysis, open **Symbol Explorer**, search for a type or method, then use **Find References** / **Go To Definition**.
6. On **Type Dependencies**, selecting a type node triggers on-demand **type member** analysis in Symbol Details.
7. Use mouse:
   - drag node: move project node
   - drag empty area: pan
   - wheel: zoom
   - double click canvas: fit to screen
8. Use graph type dropdown to switch between dependency, structure, namespace and violation graphs.
9. Click **Reload** to refresh.
