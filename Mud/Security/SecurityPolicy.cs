namespace JitRealm.Mud.Security;

/// <summary>
/// Security policy configuration for world code sandboxing.
/// Security is always enforced - no bypass options.
/// </summary>
public sealed class SecurityPolicy
{
    /// <summary>
    /// Default timeout for hook execution (OnLoad, OnEnter, OnLeave, etc.).
    /// </summary>
    public TimeSpan HookTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Default timeout for callout execution.
    /// </summary>
    public TimeSpan CalloutTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Default timeout for heartbeat execution.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Namespaces that are blocked from world code.
    /// </summary>
    public static IReadOnlySet<string> ForbiddenNamespaces { get; } = new HashSet<string>
    {
        "System.IO",
        "System.Net",
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.NetworkInformation",
        "System.Diagnostics",
        "System.Diagnostics.Process",
        "System.Reflection.Emit",
        "System.Runtime.InteropServices",
        "System.Runtime.Loader",
        "Microsoft.CodeAnalysis",
        "Microsoft.CSharp"
    };

    /// <summary>
    /// Specific types that are blocked from world code.
    /// </summary>
    public static IReadOnlySet<string> ForbiddenTypes { get; } = new HashSet<string>
    {
        "System.Environment",
        "System.AppDomain",
        "System.Activator",
        "System.Type",
        "System.Reflection.Assembly",
        "System.Reflection.MethodInfo",
        "System.Reflection.FieldInfo",
        "System.Reflection.PropertyInfo",
        "System.Reflection.ConstructorInfo",
        "System.Reflection.MemberInfo",
        "System.Reflection.BindingFlags",
        "System.Threading.Thread",
        "System.Threading.ThreadPool",
        "System.Threading.Tasks.Parallel",
        "System.Runtime.CompilerServices.Unsafe"
    };

    /// <summary>
    /// Default security policy instance.
    /// </summary>
    public static SecurityPolicy Default { get; } = new();
}
