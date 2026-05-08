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
- Select nodes and inspect details.
- Reload analysis from the same folder.
- Search and type filter.

Not included in v5:

- method call graph
- symbol-level deep analysis
- call hierarchy / IL analysis
- multi-language analysis

## Solution Layout

```text
src/
  CsArchViewer.Core/
  CsArchViewer.DotNet/
  CsArchViewer.DotNet.Roslyn/
  CsArchViewer.DotNet.SymbolAnalysis/
  CsArchViewer.Export/
  CsArchViewer.Analysis/
  CsArchViewer.Diagnostics/
  CsArchViewer.Avalonia/
```

- `CsArchViewer.Core`: graph/project models and analyzer interface.
- `CsArchViewer.DotNet`: `.sln`/`.csproj` scanner, file scanner, graph generation.
- `CsArchViewer.DotNet.Roslyn`: Roslyn namespace analyzer, rule engine, cycle detector.
- `CsArchViewer.DotNet.SymbolAnalysis`: Roslyn semantic type/file analyzers and matrix builder.
- `CsArchViewer.Export`: Mermaid / JSON / DOT exporters.
- `CsArchViewer.Analysis`: incremental engine, cache, scheduler, file tracker, explorer/search services.
- `CsArchViewer.Diagnostics`: architecture diagnostic analyzers and severity model.
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
   - left: node list
   - center: graph view
   - right: node details (project/folder/file metadata)
5. Use mouse:
   - drag node: move project node
   - drag empty area: pan
   - wheel: zoom
   - double click canvas: fit to screen
6. Use graph type dropdown to switch between dependency, structure, namespace and violation graphs.
7. Click **Reload** to refresh.

## Dependency Rules

- `CsArchViewer.Core`: no project references.
- `CsArchViewer.DotNet` -> `CsArchViewer.Core`
- `CsArchViewer.Avalonia` -> `CsArchViewer.Core`, `CsArchViewer.DotNet`
