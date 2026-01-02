using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Base class for signs and other readable, non-carryable objects.
/// Signs are fixtures in rooms that display text when read.
/// </summary>
public abstract class SignBase : MudObjectBase, IReadable, IOnLoad
{
    protected IMudContext? Ctx { get; private set; }

    /// <summary>
    /// The sign's display name (e.g., "a wooden sign").
    /// </summary>
    public abstract override string Name { get; }

    /// <summary>
    /// Short label for "You read the {label}." messages.
    /// Default returns the Name.
    /// </summary>
    public virtual string ReadableLabel => Name;

    /// <summary>
    /// The text content displayed when the sign is read.
    /// Override this to provide dynamic content.
    /// </summary>
    public abstract string ReadableText { get; }

    /// <summary>
    /// Alternative names for finding this sign with commands.
    /// </summary>
    public virtual IReadOnlyList<string> Aliases => Array.Empty<string>();

    public virtual void OnLoad(IMudContext ctx)
    {
        Ctx = ctx;
    }
}

/// <summary>
/// A simple sign with static text content.
/// </summary>
public class SimpleSign : SignBase
{
    private readonly string _name;
    private readonly string _text;
    private readonly string _label;
    private readonly string[] _aliases;

    public SimpleSign(string name, string text, string? label = null, string[]? aliases = null)
    {
        _name = name;
        _text = text;
        _label = label ?? name;
        _aliases = aliases ?? Array.Empty<string>();
    }

    public override string Name => _name;
    public override string ReadableLabel => _label;
    public override string ReadableText => _text;
    public override IReadOnlyList<string> Aliases => _aliases;
}
