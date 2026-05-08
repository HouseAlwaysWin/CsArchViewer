using CsArchViewer.Core.Models;

namespace CsArchViewer.Avalonia.ViewModels;

public sealed class NodeDetailsViewModel : ViewModelBase
{
    private string _projectName = "-";
    private string _nodeType = "-";
    private string _fullPath = "-";
    private string _targetFramework = "-";
    private string _outputType = "-";
    private string _projectReferences = "-";
    private string _packageReferences = "-";
    private string _extension = "-";
    private string _size = "-";
    private string _lastModified = "-";
    private string _childCount = "-";
    private string _referencedNamespaces = "-";
    private string _referencedBy = "-";
    private string _rule = "-";
    private string _source = "-";
    private string _target = "-";
    private string _message = "-";
    private string _fullTypeName = "-";
    private string _namespaceName = "-";
    private string _file = "-";
    private string _baseType = "-";
    private string _implementedInterfaces = "-";
    private string _referencedTypes = "-";
    private string _matrixRow = "-";
    private string _dependsOn = "-";
    private string _dependencyIncoming = "-";
    private string _dependencyCount = "0";
    private string _circularDependencyCount = "0";
    private string _violationCount = "0";
    private string _lineCount = "-";

    public string ProjectName
    {
        get => _projectName;
        private set => SetProperty(ref _projectName, value);
    }

    public string FullPath
    {
        get => _fullPath;
        private set => SetProperty(ref _fullPath, value);
    }

    public string NodeType
    {
        get => _nodeType;
        private set => SetProperty(ref _nodeType, value);
    }

    public string TargetFramework
    {
        get => _targetFramework;
        private set => SetProperty(ref _targetFramework, value);
    }

    public string OutputType
    {
        get => _outputType;
        private set => SetProperty(ref _outputType, value);
    }

    public string ProjectReferences
    {
        get => _projectReferences;
        private set => SetProperty(ref _projectReferences, value);
    }

    public string PackageReferences
    {
        get => _packageReferences;
        private set => SetProperty(ref _packageReferences, value);
    }

    public string Extension
    {
        get => _extension;
        private set => SetProperty(ref _extension, value);
    }

    public string Size
    {
        get => _size;
        private set => SetProperty(ref _size, value);
    }

    public string LastModified
    {
        get => _lastModified;
        private set => SetProperty(ref _lastModified, value);
    }

    public string ChildCount
    {
        get => _childCount;
        private set => SetProperty(ref _childCount, value);
    }

    public string ReferencedNamespaces
    {
        get => _referencedNamespaces;
        private set => SetProperty(ref _referencedNamespaces, value);
    }

    public string ReferencedBy
    {
        get => _referencedBy;
        private set => SetProperty(ref _referencedBy, value);
    }

    public string Rule
    {
        get => _rule;
        private set => SetProperty(ref _rule, value);
    }

    public string Source
    {
        get => _source;
        private set => SetProperty(ref _source, value);
    }

    public string Target
    {
        get => _target;
        private set => SetProperty(ref _target, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string FullTypeName
    {
        get => _fullTypeName;
        private set => SetProperty(ref _fullTypeName, value);
    }

    public string NamespaceName
    {
        get => _namespaceName;
        private set => SetProperty(ref _namespaceName, value);
    }

    public string File
    {
        get => _file;
        private set => SetProperty(ref _file, value);
    }

    public string BaseType
    {
        get => _baseType;
        private set => SetProperty(ref _baseType, value);
    }

    public string ImplementedInterfaces
    {
        get => _implementedInterfaces;
        private set => SetProperty(ref _implementedInterfaces, value);
    }

    public string ReferencedTypes
    {
        get => _referencedTypes;
        private set => SetProperty(ref _referencedTypes, value);
    }

    public string MatrixRow
    {
        get => _matrixRow;
        private set => SetProperty(ref _matrixRow, value);
    }

    public string DependsOn
    {
        get => _dependsOn;
        private set => SetProperty(ref _dependsOn, value);
    }

    public string DependencyIncoming
    {
        get => _dependencyIncoming;
        private set => SetProperty(ref _dependencyIncoming, value);
    }

    public string DependencyCount
    {
        get => _dependencyCount;
        private set => SetProperty(ref _dependencyCount, value);
    }

    public string CircularDependencyCount
    {
        get => _circularDependencyCount;
        private set => SetProperty(ref _circularDependencyCount, value);
    }

    public string ViolationCount
    {
        get => _violationCount;
        private set => SetProperty(ref _violationCount, value);
    }

    public string LineCount
    {
        get => _lineCount;
        private set => SetProperty(ref _lineCount, value);
    }

    public void SetProject(ProjectInfo? project)
    {
        if (project is null)
        {
            ProjectName = "-";
            NodeType = "-";
            FullPath = "-";
            TargetFramework = "-";
            OutputType = "-";
            ProjectReferences = "-";
            PackageReferences = "-";
            Extension = "-";
            Size = "-";
            LastModified = "-";
            ChildCount = "-";
            ReferencedNamespaces = "-";
            ReferencedBy = "-";
            Rule = "-";
            Source = "-";
            Target = "-";
            Message = "-";
            FullTypeName = "-";
            NamespaceName = "-";
            File = "-";
            BaseType = "-";
            ImplementedInterfaces = "-";
            ReferencedTypes = "-";
            MatrixRow = "-";
            DependsOn = "-";
            DependencyIncoming = "-";
            DependencyCount = "0";
            CircularDependencyCount = "0";
            ViolationCount = "0";
            LineCount = "-";
            return;
        }

        ProjectName = project.Name;
        NodeType = "Project";
        FullPath = project.CsProjPath;
        TargetFramework = project.TargetFramework;
        OutputType = project.OutputType;
        ProjectReferences = project.ProjectReferences.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, project.ProjectReferences);
        PackageReferences = project.PackageReferences.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, project.PackageReferences.Select(pkg => $"{pkg.Name} ({pkg.Version})"));
        Extension = "-";
        Size = "-";
        LastModified = "-";
        ChildCount = "-";
        ReferencedNamespaces = "-";
        ReferencedBy = "-";
        Rule = "-";
        Source = "-";
        Target = "-";
        Message = "-";
        FullTypeName = "-";
        NamespaceName = "-";
        File = "-";
        BaseType = "-";
        ImplementedInterfaces = "-";
        ReferencedTypes = "-";
        MatrixRow = "-";
        DependsOn = "-";
        DependencyIncoming = "-";
        DependencyCount = "0";
        CircularDependencyCount = "0";
        ViolationCount = "0";
        LineCount = "-";
    }

    public void SetNode(ArchitectureNode? node, ProjectInfo? project)
    {
        if (node is null)
        {
            SetProject(null);
            return;
        }

        if (node.Type == ArchitectureNodeType.Project)
        {
            SetProject(project);
            return;
        }

        ProjectName = node.Name;
        NodeType = node.Type.ToString();
        FullPath = node.FullPath;
        TargetFramework = node.Metadata.TryGetValue("TargetFramework", out var tfm) ? tfm : "-";
        OutputType = node.Metadata.TryGetValue("OutputType", out var outputType) ? outputType : "-";
        ProjectReferences = node.Metadata.TryGetValue("ProjectReferences", out var references) ? references : "-";
        PackageReferences = node.Metadata.TryGetValue("PackageReferences", out var packages) ? packages : "-";
        Extension = node.Metadata.TryGetValue("Extension", out var extension) ? extension : "-";
        Size = node.Metadata.TryGetValue("Size", out var size) ? size : "-";
        LastModified = node.Metadata.TryGetValue("LastModified", out var modified) ? modified : "-";
        ChildCount = node.Metadata.TryGetValue("ChildCount", out var childCount) ? childCount : "-";
        ReferencedNamespaces = node.Metadata.TryGetValue("ReferencedNamespaces", out var referencedNamespaces) ? referencedNamespaces : "-";
        ReferencedBy = node.Metadata.TryGetValue("ReferencedBy", out var referencedBy) ? referencedBy : "-";
        Rule = node.Metadata.TryGetValue("Rule", out var rule) ? rule : "-";
        Source = node.Metadata.TryGetValue("Source", out var source) ? source : "-";
        Target = node.Metadata.TryGetValue("Target", out var target) ? target : "-";
        Message = node.Metadata.TryGetValue("Message", out var message) ? message : "-";
        FullTypeName = node.Metadata.TryGetValue("FullTypeName", out var fullTypeName) ? fullTypeName : "-";
        NamespaceName = node.Metadata.TryGetValue("Namespace", out var namespaceName) ? namespaceName : "-";
        File = node.Metadata.TryGetValue("File", out var file) ? file : "-";
        BaseType = node.Metadata.TryGetValue("BaseType", out var baseType) ? baseType : "-";
        ImplementedInterfaces = node.Metadata.TryGetValue("ImplementedInterfaces", out var interfaces) ? interfaces : "-";
        ReferencedTypes = node.Metadata.TryGetValue("ReferencedTypes", out var referencedTypes) ? referencedTypes : "-";
        MatrixRow = node.Metadata.TryGetValue("MatrixRow", out var matrixRow) ? matrixRow : "-";
        DependsOn = node.Metadata.TryGetValue("DependsOn", out var dependsOn) ? dependsOn : "-";
        DependencyIncoming = node.Metadata.TryGetValue("DependencyIncoming", out var incoming) ? incoming : "-";
        DependencyCount = node.Metadata.TryGetValue("DependencyCount", out var count) ? count : "0";
        CircularDependencyCount = node.Metadata.TryGetValue("CircularDependencyCount", out var circular) ? circular : "0";
        ViolationCount = node.Metadata.TryGetValue("ViolationCount", out var violationCount) ? violationCount : "0";
        LineCount = node.Metadata.TryGetValue("LineCount", out var lineCount) ? lineCount : "-";
    }
}
