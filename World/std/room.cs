using System;
using System.Collections.Generic;
using System.Linq;
using JitRealm.Mud;

/// <summary>
/// Base class for all rooms in the game world.
/// Provides common room functionality and sensible defaults.
///
/// State stored in IStateStore (only when explicitly patched):
/// - name: Room name override (if patched)
/// - description: Room description override (if patched)
/// - visited_by: List of player IDs who have visited
/// - last_reset: When the room was last reset
/// </summary>
public abstract class RoomBase : MudObjectBase, IRoom, IOnLoad, IResettable
{
    /// <summary>
    /// Cached context for property access.
    /// Set during OnLoad.
    /// </summary>
    protected IMudContext? Ctx { get; private set; }

    /// <summary>
    /// The room's display name. Uses code default unless explicitly patched.
    /// </summary>
    public override string Name => Ctx?.State.Has("name") == true
        ? Ctx.State.Get<string>("name") ?? GetDefaultName()
        : GetDefaultName();

    /// <summary>
    /// Default name when not overridden in state. Override in subclasses.
    /// </summary>
    protected abstract string GetDefaultName();

    /// <summary>
    /// The room's description when looked at. Uses code default unless explicitly patched.
    /// </summary>
    public override string Description => Ctx?.State.Has("description") == true
        ? Ctx.State.Get<string>("description") ?? GetDefaultDescription()
        : GetDefaultDescription();

    /// <summary>
    /// Default description when not overridden in state. Override in subclasses.
    /// </summary>
    protected abstract string GetDefaultDescription();

    /// <summary>
    /// Exits from this room. Keys are direction names, values are destination IDs.
    /// </summary>
    public abstract IReadOnlyDictionary<string, string> Exits { get; }

    /// <summary>
    /// Exits that are hidden and not shown in room descriptions.
    /// Players can still use these exits if they know about them.
    /// Override in subclasses to hide secret passages or private exits.
    /// </summary>
    public virtual IReadOnlySet<string> HiddenExits => new HashSet<string>();

    /// <summary>
    /// Static contents defined in code. Actual room contents come from ContainerRegistry.
    /// </summary>
    public virtual IReadOnlyList<string> Contents => Array.Empty<string>();

    /// <summary>
    /// Whether this room is lit. Dark rooms require a light source.
    /// </summary>
    public virtual bool IsLit => true;

    /// <summary>
    /// Whether this room is outdoors (affects weather, time of day descriptions).
    /// </summary>
    public virtual bool IsOutdoors => false;

    /// <summary>
    /// Alternative names for this room (for location matching).
    /// Override in subclasses to define aliases like "shop", "store" for a shop room.
    /// </summary>
    public virtual IReadOnlyList<string> Aliases => Array.Empty<string>();

    /// <summary>
    /// Called when the room is loaded. Override for custom initialization.
    /// </summary>
    public virtual void OnLoad(IMudContext ctx)
    {
        Ctx = ctx;
        // State properties (name, description) are only used as overrides.
        // Code defaults are used unless explicitly patched via wizard commands.
    }

    /// <summary>
    /// Called when the room is reset. Override to respawn items/NPCs.
    /// </summary>
    public virtual void Reset(IMudContext ctx)
    {
        // Store last reset time
        ctx.State.Set("last_reset", ctx.Clock.Now.Ticks);
    }

    /// <summary>
    /// Get a formatted exit list string (excludes hidden exits).
    /// </summary>
    protected string GetExitString()
    {
        var visibleExits = Exits.Keys.Where(e => !HiddenExits.Contains(e)).ToList();
        if (visibleExits.Count == 0) return "There are no obvious exits.";
        return "Exits: " + string.Join(", ", visibleExits);
    }

    /// <summary>
    /// Get the long description including exits.
    /// </summary>
    public virtual string GetLongDescription()
    {
        return $"{Description}\n{GetExitString()}";
    }
}

/// <summary>
/// A simple outdoor room with default outdoor settings.
/// </summary>
public abstract class OutdoorRoomBase : RoomBase
{
    public override bool IsOutdoors => true;
    public override bool IsLit => true;
}

/// <summary>
/// An indoor room with default indoor settings.
/// </summary>
public abstract class IndoorRoomBase : RoomBase
{
    public override bool IsOutdoors => false;
    public override bool IsLit => true;
}

/// <summary>
/// A dark room that requires a light source to see.
/// </summary>
public abstract class DarkRoomBase : RoomBase
{
    public override bool IsLit => false;

    protected override string GetDefaultDescription()
    {
        // In the future, check if player has light source
        // For now, just return dark description
        return DarkDescription;
    }

    /// <summary>
    /// Description shown when the room is dark.
    /// </summary>
    protected virtual string DarkDescription => "It is too dark to see anything.";

    /// <summary>
    /// Description shown when the room is lit.
    /// </summary>
    protected abstract string LitDescription { get; }
}
