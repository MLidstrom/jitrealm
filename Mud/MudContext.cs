using JitRealm.Mud.AI;
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
    /// Optional LLM service for AI-powered NPCs.
    /// </summary>
    private readonly ILlmService? _llmService;

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
    /// <param name="llmService">Optional LLM service for AI NPCs.</param>
    public MudContext(WorldState world, IClock clock, ILlmService? llmService = null)
    {
        _internalWorld = world;
        Clock = clock;
        _llmService = llmService;
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
        var targetCtx = _internalWorld.CreateContext(targetId, Clock, _internalWorld.Containers.GetContainer(targetId));

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
        var targetCtx = _internalWorld.CreateContext(targetId, Clock, _internalWorld.Containers.GetContainer(targetId));

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
                var itemCtx = _internalWorld.CreateContext(
                    objectId,
                    Clock,
                    roomIdOverride: _internalWorld.Containers.GetContainer(currentContainer));
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
                var itemCtx = _internalWorld.CreateContext(
                    objectId,
                    Clock,
                    roomIdOverride: _internalWorld.Containers.GetContainer(destinationId));
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

            // For IItem, also check Aliases and ShortDescription
            if (obj is IItem item)
            {
                // Check aliases first (preferred lookup method)
                foreach (var alias in item.Aliases)
                {
                    if (alias.ToLowerInvariant().Contains(normalizedName) ||
                        normalizedName.Contains(alias.ToLowerInvariant()))
                        return itemId;
                }

                // Fall back to ShortDescription
                if (item.ShortDescription.ToLowerInvariant().Contains(normalizedName))
                    return itemId;
            }
        }

        return null;
    }

    // LLM methods

    public bool IsLlmEnabled => _llmService?.IsEnabled ?? false;

    public async Task<string?> LlmCompleteAsync(string systemPrompt, string userMessage)
    {
        if (_llmService is null || !_llmService.IsEnabled)
        {
            return null;
        }

        return await _llmService.CompleteAsync(systemPrompt, userMessage);
    }

    public NpcContext BuildNpcContext(ILiving npc)
    {
        var npcId = (npc as IMudObject)?.Id ?? CurrentObjectId ?? "unknown";
        var roomId = RoomId ?? _internalWorld.Containers.GetContainer(npcId);

        // Get room info
        var room = roomId is not null ? _internalWorld.Objects?.Get<IRoom>(roomId) : null;
        var roomName = room?.Name ?? "Unknown Location";
        var roomDescription = room?.Description ?? "You cannot see anything.";
        var roomExits = room?.Exits.Keys.ToList() ?? new List<string>();

        // Get combat info
        var inCombat = _internalWorld.Combat.IsInCombat(npcId);
        var combatTargetId = inCombat ? _internalWorld.Combat.GetCombatTarget(npcId) : null;
        var combatTargetName = combatTargetId is not null
            ? GetEntityName(combatTargetId)
            : null;

        // Get entities in room
        var players = new List<EntityInfo>();
        var npcs = new List<EntityInfo>();
        var items = new List<string>();

        if (roomId is not null && _internalWorld.Objects is not null)
        {
            var contents = _internalWorld.Containers.GetContents(roomId);
            foreach (var objId in contents)
            {
                if (objId == npcId) continue; // Skip self

                var obj = _internalWorld.Objects.Get<IMudObject>(objId);
                if (obj is null) continue;

                if (obj is IPlayer player)
                {
                    // It's a player
                    var session = _internalWorld.Sessions.GetByPlayerId(objId);
                    var playerName = session?.PlayerName ?? player.PlayerName;
                    players.Add(new EntityInfo
                    {
                        Id = objId,
                        Name = playerName,
                        InCombat = _internalWorld.Combat.IsInCombat(objId),
                        HP = player.HP,
                        MaxHP = player.MaxHP
                    });
                }
                else if (obj is ILiving living)
                {
                    // It's another NPC
                    npcs.Add(new EntityInfo
                    {
                        Id = objId,
                        Name = obj.Name,
                        InCombat = _internalWorld.Combat.IsInCombat(objId),
                        HP = living.HP,
                        MaxHP = living.MaxHP
                    });
                }
                else if (obj is IItem item)
                {
                    items.Add(item.ShortDescription);
                }
            }
        }

        // Get recent events
        var recentEvents = roomId is not null
            ? _internalWorld.EventLog.GetEvents(roomId, 10)
            : Array.Empty<string>();

        // Get capabilities if NPC implements ILlmNpc
        var capabilities = (npc is ILlmNpc llmNpc)
            ? llmNpc.Capabilities
            : NpcCapabilities.Humanoid;

        return new NpcContext
        {
            NpcId = npcId,
            NpcName = (npc as IMudObject)?.Name ?? "Unknown",
            CurrentHP = npc.HP,
            MaxHP = npc.MaxHP,
            InCombat = inCombat,
            CombatTargetId = combatTargetId,
            CombatTargetName = combatTargetName,
            Capabilities = capabilities,
            RoomId = roomId ?? "unknown",
            RoomName = roomName,
            RoomDescription = roomDescription,
            RoomExits = roomExits,
            PlayersInRoom = players,
            NpcsInRoom = npcs,
            ItemsInRoom = items,
            RecentEvents = recentEvents.ToList()
        };
    }

    public void RecordEvent(string eventDescription)
    {
        var roomId = RoomId ?? (CurrentObjectId is not null
            ? _internalWorld.Containers.GetContainer(CurrentObjectId)
            : null);

        if (roomId is not null)
        {
            _internalWorld.EventLog.Record(roomId, eventDescription);
        }
    }

    private string GetEntityName(string entityId)
    {
        // Check if it's a player first
        var session = _internalWorld.Sessions.GetByPlayerId(entityId);
        if (session?.PlayerName is not null)
            return session.PlayerName;

        // Otherwise get the object name
        var obj = _internalWorld.Objects?.Get<IMudObject>(entityId);
        return obj?.Name ?? entityId;
    }

    // NPC command execution

    public async Task<bool> ExecuteCommandAsync(string command)
    {
        if (CurrentObjectId is null)
            return false;

        return await _internalWorld.NpcCommands.ExecuteAsync(CurrentObjectId, command);
    }

    public async Task ExecuteLlmResponseAsync(string response, bool canSpeak, bool canEmote)
    {
        if (CurrentObjectId is null)
            return;

        await _internalWorld.NpcCommands.ExecuteLlmResponseAsync(CurrentObjectId, response, canSpeak, canEmote);
    }
}
