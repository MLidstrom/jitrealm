using JitRealm.Mud.AI;
using JitRealm.Mud.Security;
using Pgvector;

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

        // COIN MERGE: If moving a coin to a container that already has coins of same material, merge
        if (obj is ICoin incomingCoin)
        {
            var existingCoinId = FindCoinPile(destinationId, incomingCoin.Material);
            if (existingCoinId is not null && existingCoinId != objectId)
            {
                // Add amount to existing pile
                var existingState = _internalWorld.Objects.GetStateStore(existingCoinId);
                var existingAmount = existingState?.Get<int>("amount") ?? 0;
                existingState?.Set("amount", existingAmount + incomingCoin.Amount);

                // Remove incoming coin from container and destruct
                _internalWorld.Containers.Remove(objectId);
                _internalWorld.Objects.DestructAsync(objectId, _internalWorld).GetAwaiter().GetResult();

                return true;  // Merged successfully
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

    /// <summary>
    /// Find a coin pile of specific material in a container.
    /// </summary>
    private string? FindCoinPile(string containerId, CoinMaterial material)
    {
        var contents = _internalWorld.Containers.GetContents(containerId);
        foreach (var itemId in contents)
        {
            var coin = _internalWorld.Objects?.Get<ICoin>(itemId);
            if (coin?.Material == material)
                return itemId;
        }
        return null;
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

        var debugger = _internalWorld.LlmDebugger;
        var npcId = CurrentObjectId ?? "unknown";

        debugger?.LogRequest(npcId, null, systemPrompt, userMessage);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var response = await _llmService.CompleteAsync(systemPrompt, userMessage);

        sw.Stop();
        debugger?.LogResponse(npcId, response, (int)sw.ElapsedMilliseconds);

        return response;
    }

    public async Task<NpcContext> BuildNpcContextAsync(ILiving npc, string? focalPlayerName = null, string? memoryQueryText = null)
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

        // Get NPC's inventory
        var inventory = new List<string>();
        if (_internalWorld.Objects is not null)
        {
            var npcContents = _internalWorld.Containers.GetContents(npcId);
            foreach (var itemId in npcContents)
            {
                var item = _internalWorld.Objects.Get<IItem>(itemId);
                if (item is not null)
                {
                    inventory.Add(item.ShortDescription);
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

        // Long-term per-NPC memory + world KB (optional)
        var longTermMemories = Array.Empty<string>();
        var worldFacts = Array.Empty<string>();
        var drives = new List<string>();
        string? goalSummary = null;
        int goalCount = 0;

        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is not null)
        {
            try
            {
                // Goals (small; include a few top priorities)
                var goals = await memorySystem.Goals.GetAllAsync(npcId);
                if (goals.Count > 0)
                {
                    var activeGoals = goals
                        .Where(g => string.Equals(g.Status, "active", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(g => g.Importance)
                        .Take(5)  // Show up to 5 goals to LLM
                        .ToList();

                    goalCount = activeGoals.Count;
                    if (goalCount > 0)
                    {
                        var goalParts = activeGoals.Select(g =>
                        {
                            var plan = GoalPlan.FromParams(g.Params);
                            var planSummary = plan.HasPlan ? $" ({plan.GetContextSummary()})" : "";

                            return string.IsNullOrWhiteSpace(g.TargetPlayer)
                                ? $"[{g.Importance}] {g.GoalType}{planSummary}"
                                : $"[{g.Importance}] {g.GoalType} -> {g.TargetPlayer}{planSummary}";
                        });
                        goalSummary = string.Join("; ", goalParts);
                    }
                }

                // Memories (bounded)
                var memTopK = memorySystem.DefaultMemoryTopK;

                // Semantic query embedding (optional)
                Vector? queryEmbedding = null;
                if (!string.IsNullOrWhiteSpace(memoryQueryText) && memorySystem.IsSemanticSearchEnabled)
                {
                    queryEmbedding = await memorySystem.EmbedQueryAsync(memoryQueryText);
                }

                var mems = await memorySystem.NpcMemory.RecallAsync(new NpcMemoryRecallQuery(
                    NpcId: npcId,
                    SubjectPlayer: focalPlayerName,
                    Tags: Array.Empty<string>(),
                    TopK: memTopK,
                    CandidateLimit: memorySystem.CandidateLimit,
                    QueryEmbedding: queryEmbedding));
                if (mems.Count > 0)
                {
                    longTermMemories = mems
                        .Select(m => $"{m.Kind}: {m.Content}")
                        .ToArray();
                }

                // World KB (tagged by room/area, best-effort)
                var kbTags = roomId is not null
                    ? new[] { $"room:{roomId}", $"area:{roomId.Split('/').FirstOrDefault() ?? roomId}" }
                    : Array.Empty<string>();
                if (kbTags.Length > 0)
                {
                    var kb = await memorySystem.WorldKnowledge.SearchByTagsAsync(kbTags, memorySystem.DefaultKbTopK);
                    if (kb.Count > 0)
                    {
                        worldFacts = kb
                            .Select(e => $"{e.Key}: {e.Value.RootElement.ToString()}")
                            .ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Memory] WARNING: Failed to retrieve memory/goal/KB for {npcId}: {ex.Message}");
            }
        }

        // Drives / needs
        // Always include survive as top drive, but prefer persisted needs list when available.
        try
        {
            var memorySystem2 = _internalWorld.MemorySystem;
            if (memorySystem2 is not null)
            {
                var needs = await memorySystem2.Needs.GetAllAsync(npcId);
                foreach (var n in needs.OrderBy(n => n.Level))
                {
                    drives.Add($"{n.NeedType} (level {n.Level})");
                }
            }
        }
        catch
        {
            // Ignore; fall back below
        }

        // Ensure survive shows urgency based on combat/health (even if needs aren't available yet).
        var hpPercent2 = npc.MaxHP > 0 ? (npc.HP * 100 / npc.MaxHP) : 100;
        var surviveUrgency = (inCombat || hpPercent2 <= 25)
            ? "survive: critical (seek safety, flee/defend)"
            : hpPercent2 <= 50
                ? "survive: injured (be cautious, avoid risk)"
                : "survive: stable";

        // Put survive first, de-duping any persisted "survive (level 1)" entry.
        drives.RemoveAll(d => d.StartsWith("survive", StringComparison.OrdinalIgnoreCase));
        drives.Insert(0, surviveUrgency);

        // Log context retrieval statistics
        _internalWorld.LlmDebugger?.LogContext(
            npcId,
            memoryCount: longTermMemories.Length,
            goalCount: goalCount,
            needCount: drives.Count,
            kbCount: worldFacts.Length);

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
            Inventory = inventory,
            RoomId = roomId ?? "unknown",
            RoomName = roomName,
            RoomDescription = roomDescription,
            RoomExits = roomExits,
            PlayersInRoom = players,
            NpcsInRoom = npcs,
            ItemsInRoom = items,
            RecentEvents = recentEvents.ToList(),
            LongTermMemories = longTermMemories,
            WorldKnowledge = worldFacts,
            Drives = drives,
            GoalSummary = goalSummary
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

    public async Task ExecuteLlmResponseAsync(string response, bool canSpeak, bool canEmote, string? interactorId = null)
    {
        if (CurrentObjectId is null)
            return;

        await _internalWorld.NpcCommands.ExecuteLlmResponseAsync(CurrentObjectId, response, canSpeak, canEmote, interactorId);
    }

    // Coin methods

    public int GetCopperValue(string containerId)
    {
        return CoinHelper.GetTotalCopperValue(_internalWorld, containerId);
    }

    public async Task AddCoinsAsync(string containerId, int copperAmount)
    {
        if (copperAmount <= 0)
            return;

        // Calculate optimal breakdown
        var gold = copperAmount / 10000;
        var remaining = copperAmount % 10000;
        var silver = remaining / 100;
        var copper = remaining % 100;

        // Add each denomination
        if (gold > 0)
            await CoinHelper.AddCoinsAsync(_internalWorld, containerId, gold, CoinMaterial.Gold);
        if (silver > 0)
            await CoinHelper.AddCoinsAsync(_internalWorld, containerId, silver, CoinMaterial.Silver);
        if (copper > 0)
            await CoinHelper.AddCoinsAsync(_internalWorld, containerId, copper, CoinMaterial.Copper);
    }

    public async Task<bool> DeductCoinsAsync(string containerId, int copperAmount)
    {
        return await CoinHelper.DeductCoinsAsync(_internalWorld, containerId, copperAmount);
    }

    // Goal methods

    public async Task<bool> SetGoalAsync(string goalType, string? targetPlayer = null, string status = "active", int importance = 50)
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return false;

        // Drives are not persisted as goals.
        if (string.Equals(goalType, "survive", StringComparison.OrdinalIgnoreCase))
            return false;

        var goal = new NpcGoal(
            NpcId: CurrentObjectId,
            GoalType: goalType,
            TargetPlayer: targetPlayer,
            Params: System.Text.Json.JsonDocument.Parse("{}"),
            Status: status,
            Importance: importance,
            UpdatedAt: DateTimeOffset.UtcNow);

        await memorySystem.Goals.UpsertAsync(goal);
        return true;
    }

    public async Task<bool> ClearGoalAsync(string goalType)
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return false;

        await memorySystem.Goals.ClearAsync(CurrentObjectId, goalType);
        return true;
    }

    public async Task<bool> ClearAllGoalsAsync(bool preserveSurvival = true)
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return false;

        // Survive is a drive, not a goal. Clear all goals (including any legacy survive rows).
        await memorySystem.Goals.ClearAllAsync(CurrentObjectId, preserveSurvival: false);
        return true;
    }

    public async Task<NpcGoal?> GetGoalAsync()
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return null;

        return await memorySystem.Goals.GetAsync(CurrentObjectId);
    }

    public async Task<IReadOnlyList<NpcGoal>> GetAllGoalsAsync()
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return Array.Empty<NpcGoal>();

        return await memorySystem.Goals.GetAllAsync(CurrentObjectId);
    }

    // Need/drive methods

    public async Task<bool> SetNeedAsync(string needType, int level = 1, string status = "active")
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return false;

        var need = new NpcNeed(
            NpcId: CurrentObjectId,
            NeedType: needType,
            Level: Math.Max(1, level),
            Params: System.Text.Json.JsonDocument.Parse("{}"),
            Status: status,
            UpdatedAt: DateTimeOffset.UtcNow);

        await memorySystem.Needs.UpsertAsync(need);
        return true;
    }

    public async Task<bool> ClearNeedAsync(string needType)
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return false;

        await memorySystem.Needs.ClearAsync(CurrentObjectId, needType);
        return true;
    }

    public async Task<IReadOnlyList<NpcNeed>> GetAllNeedsAsync()
    {
        var memorySystem = _internalWorld.MemorySystem;
        if (memorySystem is null || CurrentObjectId is null)
            return Array.Empty<NpcNeed>();

        return await memorySystem.Needs.GetAllAsync(CurrentObjectId);
    }
}
