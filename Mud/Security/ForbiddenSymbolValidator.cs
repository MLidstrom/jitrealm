using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JitRealm.Mud.Security;

/// <summary>
/// Validates compiled code to ensure it doesn't use forbidden namespaces or types.
/// Uses Roslyn semantic analysis to detect dangerous API usage.
/// </summary>
public sealed class ForbiddenSymbolValidator
{
    /// <summary>
    /// Result of validation.
    /// </summary>
    public sealed class ValidationResult
    {
        public bool IsValid { get; init; }
        public List<string> Errors { get; init; } = new();

        public static ValidationResult Success => new() { IsValid = true };

        public static ValidationResult Failure(IEnumerable<string> errors) =>
            new() { IsValid = false, Errors = errors.ToList() };
    }

    /// <summary>
    /// Validates a compilation for forbidden symbols.
    /// </summary>
    /// <param name="compilation">The Roslyn compilation to validate.</param>
    /// <returns>Validation result with any errors found.</returns>
    public ValidationResult Validate(CSharpCompilation compilation)
    {
        var errors = new List<string>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            // Check using directives
            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            foreach (var usingDirective in usings)
            {
                var namespaceName = usingDirective.Name?.ToString();
                if (namespaceName is not null && IsForbiddenNamespace(namespaceName))
                {
                    var location = usingDirective.GetLocation();
                    var line = location.GetLineSpan().StartLinePosition.Line + 1;
                    errors.Add($"Line {line}: Forbidden namespace '{namespaceName}' is not allowed in world code");
                }
            }

            // Check identifier references (member access, type references, etc.)
            var identifiers = root.DescendantNodes()
                .Where(n => n is IdentifierNameSyntax or MemberAccessExpressionSyntax or QualifiedNameSyntax);

            foreach (var node in identifiers)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol is null) continue;

                // Check containing namespace
                var containingNamespace = GetFullNamespace(symbol.ContainingNamespace);
                if (IsForbiddenNamespace(containingNamespace))
                {
                    var location = node.GetLocation();
                    var line = location.GetLineSpan().StartLinePosition.Line + 1;
                    errors.Add($"Line {line}: Access to '{symbol.Name}' in forbidden namespace '{containingNamespace}' is not allowed");
                }

                // Check if the symbol itself is a forbidden type
                var fullTypeName = GetFullTypeName(symbol);
                if (fullTypeName is not null && IsForbiddenType(fullTypeName))
                {
                    var location = node.GetLocation();
                    var line = location.GetLineSpan().StartLinePosition.Line + 1;
                    errors.Add($"Line {line}: Access to forbidden type '{fullTypeName}' is not allowed");
                }
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success
            : ValidationResult.Failure(errors.Distinct());
    }

    private static bool IsForbiddenNamespace(string? namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
            return false;

        foreach (var forbidden in SecurityPolicy.ForbiddenNamespaces)
        {
            if (namespaceName.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
                namespaceName.StartsWith(forbidden + ".", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForbiddenType(string fullTypeName)
    {
        return SecurityPolicy.ForbiddenTypes.Contains(fullTypeName);
    }

    private static string GetFullNamespace(INamespaceSymbol? ns)
    {
        if (ns is null || ns.IsGlobalNamespace)
            return string.Empty;

        var parts = new List<string>();
        var current = ns;

        while (current is not null && !current.IsGlobalNamespace)
        {
            parts.Insert(0, current.Name);
            current = current.ContainingNamespace;
        }

        return string.Join(".", parts);
    }

    private static string? GetFullTypeName(ISymbol symbol)
    {
        ITypeSymbol? typeSymbol = symbol switch
        {
            ITypeSymbol ts => ts,
            IMethodSymbol ms => ms.ContainingType,
            IPropertySymbol ps => ps.ContainingType,
            IFieldSymbol fs => fs.ContainingType,
            _ => null
        };

        if (typeSymbol is null)
            return null;

        var ns = GetFullNamespace(typeSymbol.ContainingNamespace);
        return string.IsNullOrEmpty(ns)
            ? typeSymbol.Name
            : $"{ns}.{typeSymbol.Name}";
    }
}
