namespace CsArchViewer.Core.Models;

public enum ArchitectureEdgeType
{
    Contains,
    ProjectReference,
    PackageReference,
    UsesNamespace,
    ViolatesRule,
    CircularDependency,
    UsesType,
    Inherits,
    Implements,
    UsesFile
}
