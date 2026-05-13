using System.Xml.Linq;
using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet;

public sealed class CsProjParser
{
    public ProjectInfo Parse(string csProjPath)
    {
        var document = XDocument.Load(csProjPath);
        var projectElement = document.Root ?? throw new InvalidOperationException("Invalid csproj xml.");

        var targetFramework = projectElement
            .Descendants("TargetFramework")
            .Select(x => x.Value.Trim())
            .FirstOrDefault()
            ?? projectElement
                .Descendants("TargetFrameworks")
                .Select(x => x.Value.Trim())
                .FirstOrDefault();

        var outputType = projectElement
            .Descendants("OutputType")
            .Select(x => x.Value.Trim())
            .FirstOrDefault();

        var projectReferences = projectElement
            .Descendants("ProjectReference")
            .Select(x => x.Attribute("Include")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => NormalizeProjectReference(csProjPath, x!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var packageReferences = projectElement
            .Descendants("PackageReference")
            .Select(x => new PackageInfo
            {
                Name = x.Attribute("Include")?.Value?.Trim() ?? string.Empty,
                Version = x.Attribute("Version")?.Value?.Trim() ?? x.Element("Version")?.Value?.Trim() ?? string.Empty
            })
            .Where(pkg => !string.IsNullOrWhiteSpace(pkg.Name))
            .OrderBy(pkg => pkg.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(csProjPath),
            CsProjPath = csProjPath,
            TargetFramework = string.IsNullOrWhiteSpace(targetFramework) ? "unknown" : targetFramework,
            OutputType = string.IsNullOrWhiteSpace(outputType) ? "Library" : outputType,
            ProjectReferences = projectReferences,
            PackageReferences = packageReferences
        };
    }

    private static string NormalizeProjectReference(string csProjPath, string includePath)
    {
        var projectDirectory = Path.GetDirectoryName(csProjPath) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(projectDirectory, includePath));
    }
}
