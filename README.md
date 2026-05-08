# CsArchViewer

Lightweight .NET architecture explorer built with Avalonia.

## Scope (v5 MVP)

- Scan a folder for `.sln` and `.csproj`.
- Parse project-level data:
  - `ProjectReference`
  - `PackageReference`
  - `TargetFramework`
  - `OutputType`
- Build and display graph views:
  - Project Dependencies
  - Package Dependencies
  - Folder Structure
  - File Structure
  - Namespace Dependencies (Roslyn-based)
  - Architecture Violations
  - Type Dependencies
  - File Dependencies
  - Dependency Matrix
- Run architecture rules against namespace dependencies.
- Detect circular namespace dependencies.
- Detect circular type dependencies.
- Export graph as Mermaid / JSON / Graphviz DOT.
- Incremental analysis engine with file hash cache.
- Background analysis queue (non-blocking UI).
- Live file watcher for `.cs`, `.csproj`, `.sln`.
- Dependency explorer metadata (depends-on / referenced-by / counts).
- Diagnostics pipeline:
  - CircularDependency
  - LayerViolation
  - DependencyDepthWarning
  - UnusedReference
- Code Metrics / LOC Analysis:
  - File LOC / Code LOC / Comment LOC / Blank LOC
  - Project metrics / Namespace metrics / Dependency metrics
  - Architecture health warnings:
    - LargeFileWarning
    - LargeNamespaceWarning
    - HighDependencyWarning
    - CircularDependencyWarning
    - DeepDependencyWarning
- Metrics Dashboard (Avalonia)
- Graph overlay modes:
  - Dependency Count
  - LOC Heatmap
  - Project Size
  - Diagnostics Severity
- Metrics filters:
  - Large Files
  - Highly Coupled
  - Circular Dependencies
  - High Dependency Depth
- Metrics export:
  - Metrics JSON
  - Metrics CSV
  - Metrics Markdown report
- Select nodes and inspect details.
- Reload analysis from the same folder.
- Search and type filter.

Not included in v5:

- method call graph
- symbol-level deep analysis
- call hierarchy / IL analysis
- multi-language analysis

## Scope (v6 MVP — Architecture Explorer + lightweight code intelligence)

Adds **`CsArchViewer.DotNet.SymbolExplorer`** (no Avalonia dependency):

- **Roslyn symbol index** over C# compilation documents (`SemanticModel` / `ISymbol`): types, methods, properties, fields, events, namespaces.
- **Incremental index refresh**: full rebuild after complete analysis; per-file re-index when `.cs` files change (best-effort on top of the cached workspace snapshot).
- **Symbol Explorer UI** (workspace tab): fuzzy-ish search, results list, symbol summary.
- **Find References** via `SymbolFinder` → locations listed in bottom tab; double-click or **Open Selected** uses OS default editor (path + line preserved in model).
- **On-demand type analysis**: selecting a **graph type node** (Type Dependencies et al.) or a **type symbol** loads methods/properties/fields/events, base type, interfaces.
- **Lightweight method metadata**: signature, accessibility, async/static/virtual flags, parameters, return type, **used types only** (no global method-call graph).
- **Navigation helpers**: jump to definition for symbol / selected method.

Explicitly **not** in v6:

- Full method call graph, IL analysis, profiler, runtime tracing, ReSharper-grade refactoring.

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

## Build

```powershell
dotnet build .\CsArchViewer.sln
```

## Run

```powershell
dotnet run --project .\src\CsArchViewer.Avalonia\CsArchViewer.Avalonia.csproj
```

## Usage

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

## Dependency Rules

- `CsArchViewer.Core`: no project references.
- `CsArchViewer.DotNet` -> `CsArchViewer.Core`
- `CsArchViewer.Avalonia` -> `CsArchViewer.Core`, `CsArchViewer.DotNet`, `CsArchViewer.DotNet.SymbolExplorer`
