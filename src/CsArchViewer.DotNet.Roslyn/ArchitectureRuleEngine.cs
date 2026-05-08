using System.Text.Json;
using System.Text.RegularExpressions;
using CsArchViewer.Core.Models;

namespace CsArchViewer.DotNet.Roslyn;

public sealed class ArchitectureRuleEngine
{
    public sealed record ArchitectureRule(
        string SourcePattern,
        string ForbiddenTargetPattern,
        string Severity,
        string RuleCategory,
        string Message);
    public sealed record RuleViolation(string SourceNamespace, string TargetNamespace, ArchitectureRule Rule);

    public IReadOnlyList<ArchitectureRule> LoadRules(string rulesFilePath)
    {
        if (!File.Exists(rulesFilePath))
        {
            return GetDefaultRules();
        }

        var json = File.ReadAllText(rulesFilePath);
        var records = JsonSerializer.Deserialize<List<RuleRecord>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        var rules = records
            .Where(record => !string.IsNullOrWhiteSpace(record.Source) && !string.IsNullOrWhiteSpace(record.Forbidden))
            .Select(record => new ArchitectureRule(
                record.Source!,
                record.Forbidden!,
                string.IsNullOrWhiteSpace(record.Severity) ? "Error" : record.Severity!,
                string.IsNullOrWhiteSpace(record.RuleCategory) ? "Layering" : record.RuleCategory!,
                record.Message ?? "Architecture rule violation"))
            .ToList();

        return rules.Count > 0 ? rules : GetDefaultRules();
    }

    public IReadOnlyList<RuleViolation> Evaluate(ArchitectureGraph namespaceGraph, IReadOnlyList<ArchitectureRule> rules)
    {
        var violations = new List<RuleViolation>();
        foreach (var edge in namespaceGraph.Edges.Where(edge => edge.Type == ArchitectureEdgeType.UsesNamespace))
        {
            foreach (var rule in rules)
            {
                if (!MatchesPattern(edge.FromNodeId, rule.SourcePattern))
                {
                    continue;
                }

                if (!MatchesPattern(edge.ToNodeId, rule.ForbiddenTargetPattern))
                {
                    continue;
                }

                violations.Add(new RuleViolation(edge.FromNodeId, edge.ToNodeId, rule));
            }
        }

        return violations;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
               value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ArchitectureRule> GetDefaultRules()
    {
        return
        [
            new ArchitectureRule("CsArchViewer.Core*", "*Avalonia*", "Error", "Layering", "Core layer cannot depend on Avalonia"),
            new ArchitectureRule("CsArchViewer.DotNet*", "*Avalonia*", "Warning", "Layering", "Analyzer layer cannot depend on UI")
        ];
    }

    private sealed class RuleRecord
    {
        public string? Source { get; init; }
        public string? Forbidden { get; init; }
        public string? Severity { get; init; }
        public string? RuleCategory { get; init; }
        public string? Message { get; init; }
    }
}
