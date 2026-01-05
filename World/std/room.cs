using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// Base class for all rooms in the game world.
/// Provides common room functionality and sensible defaults.
///
/// State stored in IStateStore:
/// - visited_by: List of player IDs who have visited
/// - last_reset: When the room was last reset
/// </summary>
public abstract class RoomBase : MudObjectBase, IRoom, IOnLoad, IResettable
{
    /// <summary>
    /// The room's display name. Override to customize.
    /// </summary>
    public abstract override string Name { get; }

    /// <summary>
    /// The room's description when looked at.
    /// </summary>
    public abstract override string Description { get; }

    /// <summary>
    /// Exits from this room. Keys are direction names, values are destination IDs.
    /// </summary>
    public abstract IReadOnlyDictionary<string, string> Exits { get; }

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
    /// Called when the room is loaded. Override for custom initialization.
    /// </summary>
    public virtual void OnLoad(IMudContext ctx)
    {
        // Base implementation does nothing
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
    /// Get a formatted exit list string.
    /// </summary>
    protected string GetExitString()
    {
        if (Exits.Count == 0) return "There are no obvious exits.";
        return "Exits: " + string.Join(", ", Exits.Keys);
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

    public override string Description
    {
        get
        {
            // In the future, check if player has light source
            // For now, just return dark description
            return DarkDescription;
        }
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
