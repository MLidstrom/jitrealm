using JitRealm.Mud.Security;

namespace JitRealm.Mud;

/// <summary>
/// Implementation of IMudContext, providing world code access to driver services.
/// Internally holds WorldState but only exposes sandboxed access to world code.
/// </summary>
public sealed class MudContext : IMudContext
{
    /// <summary>
    /// Internal reference to the full WorldState (not exposed to world code).
    /// </summary>
    private readonly WorldState _internalWorld;

    /// <summary>
    /// Sandboxed world access exposed to world code.
    /// </summary>
    public ISandboxedWorldAccess World { get; }

    public required IStateStore State { get; init; }
    public IClock Clock { get; }

    /// <summary>
    /// The ID of the current object this context is associated with.
    /// </summary>
    public string? CurrentObjectId { get; init; }

    /// <summary>
    /// The room ID where this context's object is located (for Say/Emote).
    /// For rooms, this is typically the room's own ID.
    /// </summary>
    public string? RoomId { get; init; }

    /// <summary>
    /// Creates a new MudContext with the specified world state.
    /// </summary>
    /// <param name="world">The internal world state.</param>
    /// <param name="clock">The clock for time access.</param>
    public MudContext(WorldState world, IClock clock)
    {
        _internalWorld = world;
        Clock = clock;
        World = new SandboxedWorldAccess(world, clock);
    }

    public void Tell(string targetId, string message)
    {
        var fromId = CurrentObjectId ?? "unknown";
        _internalWorld.Messages.Enqueue(new MudMessage(fromId, targetId, MessageType.Tell, message, null));
    }

    public void Say(string message)
    {
        var fromId = CurrentObjectId ?? "unknown";
        // Use RoomId, or try to find it via ContainerRegistry for the current object
        var roomId = RoomId ?? (CurrentObjectId is not null ? _internalWorld.Containers.GetContainer(CurrentObjectId) : null);
        _internalWorld.Messages.Enqueue(new MudMessage(fromId, null, MessageType.Say, message, roomId));
    }

    public void Emote(string action)
    {
        var fromId = CurrentObjectId ?? "unknown";
        // Use RoomId, or try to find it via ContainerRegistry for the current object
        var roomId = RoomId ?? (CurrentObjectId is not null ? _internalWorld.Containers.GetContainer(CurrentObjectId) : null);
        _internalWorld.Messages.Enqueue(new MudMessage(fromId, null, MessageType.Emote, action, roomId));
    }

    public long CallOut(string methodName, TimeSpan delay, params object?[] args)
    {
        var targetId = CurrentObjectId ?? throw new InvalidOperationException("No current object for CallOut");
        return _internalWorld.CallOuts.Schedule(targetId, methodName, delay, args.Length > 0 ? args : null);
    }

    public long Every(string methodName, TimeSpan interval, params object?[] args)
    {
        var targetId = CurrentObjectId ?? throw new InvalidOperationException("No current object for Every");
        return _internalWorld.CallOuts.ScheduleEvery(targetId, methodName, interval, args.Length > 0 ? args : null);
    }

    public bool CancelCallOut(long calloutId)
    {
        return _internalWorld.CallOuts.Cancel(calloutId);
    }

    public bool DealDamage(string targetId, int amount)
    {
        if (_internalWorld.Objects is null)
            return false;

        var target = _internalWorld.Objects.Get<ILiving>(targetId);
        if (target is null)
            return false;

        // Create a context for the target object
        var targetCtx = new MudContext(_internalWorld, Clock)
        {
            State = _internalWorld.Objects.GetStateStore(targetId) ?? new DictionaryStateStore(),
            CurrentObjectId = targetId,
            RoomId = _internalWorld.Containers.GetContainer(targetId)
        };

        target.TakeDamage(amount, CurrentObjectId, targetCtx);
        return true;
    }

    public bool HealTarget(string targetId, int amount)
    {
        if (_internalWorld.Objects is null)
            return false;

        var target = _internalWorld.Objects.Get<ILiving>(targetId);
        if (target is null)
            return false;

        // Create a context for the target object
        var targetCtx = new MudContext(_internalWorld, Clock)
        {
            State = _internalWorld.Objects.GetStateStore(targetId) ?? new DictionaryStateStore(),
            CurrentObjectId = targetId,
            RoomId = _internalWorld.Containers.GetContainer(targetId)
        };

        target.Heal(amount, targetCtx);
        return true;
    }

    public bool Move(string objectId, string destinationId)
    {
        if (_internalWorld.Objects is null)
            return false;

        var obj = _internalWorld.Objects.Get<IMudObject>(objectId);
        if (obj is null)
            return false;

        // Call OnDrop if moving from a living's inventory
        var currentContainer = _internalWorld.Containers.GetContainer(objectId);
        if (currentContainer is not null && obj is ICarryable carryable)
        {
            var containerObj = _internalWorld.Objects.Get<ILiving>(currentContainer);
            if (containerObj is not null)
            {
                // Create context for the item
                var itemCtx = new MudContext(_internalWorld, Clock)
                {
                    State = _internalWorld.Objects.GetStateStore(objectId) ?? new DictionaryStateStore(),
                    CurrentObjectId = objectId,
                    RoomId = _internalWorld.Containers.GetContainer(currentContainer)
                };
                carryable.OnDrop(itemCtx, currentContainer);
            }
        }

        // Move the object
        _internalWorld.Containers.Move(objectId, destinationId);

        // Call OnGet if moving to a living's inventory
        if (obj is ICarryable carryable2)
        {
            var destObj = _internalWorld.Objects.Get<ILiving>(destinationId);
            if (destObj is not null)
            {
                // Create context for the item
                var itemCtx = new MudContext(_internalWorld, Clock)
                {
                    State = _internalWorld.Objects.GetStateStore(objectId) ?? new DictionaryStateStore(),
                    CurrentObjectId = objectId,
                    RoomId = _internalWorld.Containers.GetContainer(destinationId)
                };
                carryable2.OnGet(itemCtx, destinationId);
            }
        }

        return true;
    }

    public int GetContainerWeight(string containerId)
    {
        if (_internalWorld.Objects is null)
            return 0;

        int totalWeight = 0;
        var contents = _internalWorld.Containers.GetContents(containerId);
        foreach (var itemId in contents)
        {
            var item = _internalWorld.Objects.Get<IItem>(itemId);
            if (item is not null)
            {
                totalWeight += item.Weight;
            }
        }
        return totalWeight;
    }

    public IReadOnlyCollection<string> GetInventory()
    {
        if (CurrentObjectId is null)
            return Array.Empty<string>();

        return _internalWorld.Containers.GetContents(CurrentObjectId);
    }

    public string? FindItem(string name, string containerId)
    {
        if (_internalWorld.Objects is null)
            return null;

        var normalizedName = name.ToLowerInvariant();
        var contents = _internalWorld.Containers.GetContents(containerId);

        foreach (var itemId in contents)
        {
            var obj = _internalWorld.Objects.Get<IMudObject>(itemId);
            if (obj is null)
                continue;

            // Check if item name contains the search term (case-insensitive)
            if (obj.Name.ToLowerInvariant().Contains(normalizedName))
                return itemId;

            // Also check ShortDescription for IItem
            if (obj is IItem item && item.ShortDescription.ToLowerInvariant().Contains(normalizedName))
                return itemId;
        }

        return null;
    }
}
