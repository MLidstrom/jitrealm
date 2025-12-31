using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace JitRealm.Mud.Security;

/// <summary>
/// Provides a curated list of safe assembly references for world code compilation.
/// Only assemblies necessary for legitimate world functionality are included.
/// </summary>
public static class SafeReferences
{
    /// <summary>
    /// Assembly names that are allowed for world code compilation.
    /// These are the essential .NET runtime assemblies plus JitRealm interfaces.
    /// </summary>
    private static readonly HashSet<string> AllowedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core runtime
        "System.Runtime",
        "System.Runtime.Extensions",
        "System.Private.CoreLib",
        "netstandard",

        // Collections
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Collections.Immutable",

        // LINQ
        "System.Linq",
        "System.Linq.Expressions",

        // Text and JSON (for state serialization)
        "System.Text.Json",
        "System.Text.Encoding.Extensions",
        "System.Text.RegularExpressions",

        // Memory and buffers
        "System.Memory",
        "System.Buffers",

        // Basic utilities
        "System.Console", // For basic output during development
        "System.ObjectModel",
        "System.ComponentModel",
        "System.ComponentModel.Primitives",

        // Threading primitives (limited - no Thread/ThreadPool)
        "System.Threading",
        "System.Threading.Tasks",

        // Numerics
        "System.Numerics.Vectors",

        // Globalization
        "System.Globalization"
    };

    /// <summary>
    /// Gets the safe metadata references for world code compilation.
    /// </summary>
    /// <returns>List of safe assembly references.</returns>
    public static IReadOnlyList<MetadataReference> GetSafeReferences() => Cached.Value;

    private static readonly Lazy<ImmutableArray<MetadataReference>> Cached = new(() =>
    {
        var refs = new List<MetadataReference>();

        // Get trusted platform assemblies
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(path);

                // Only include whitelisted assemblies
                if (IsAllowedAssembly(assemblyName))
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        // Always include JitRealm assembly for interfaces
        var hostAsm = typeof(IMudObject).Assembly;
        if (!string.IsNullOrWhiteSpace(hostAsm.Location))
        {
            refs.Add(MetadataReference.CreateFromFile(hostAsm.Location));
        }

        return refs.ToImmutableArray();
    });

    /// <summary>
    /// Checks if an assembly name is in the allowed list.
    /// </summary>
    private static bool IsAllowedAssembly(string assemblyName)
    {
        // Check exact match first
        if (AllowedAssemblyNames.Contains(assemblyName))
            return true;

        // Block anything with dangerous prefixes
        if (assemblyName.StartsWith("System.IO", StringComparison.OrdinalIgnoreCase))
            return false;
        if (assemblyName.StartsWith("System.Net", StringComparison.OrdinalIgnoreCase))
            return false;
        if (assemblyName.StartsWith("System.Diagnostics", StringComparison.OrdinalIgnoreCase))
            return false;
        if (assemblyName.StartsWith("System.Reflection.Emit", StringComparison.OrdinalIgnoreCase))
            return false;
        if (assemblyName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase))
            return false;

        return false;
    }
}
