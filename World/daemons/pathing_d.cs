using System;
using System.Collections.Generic;
using JitRealm.Mud;

/// <summary>
/// PATHING_D - Room pathfinding daemon.
/// Provides BFS-based path finding for NPCs to navigate between rooms.
/// Uses caching to avoid recomputing common routes.
///
/// Access from world code: ctx.World.GetDaemon&lt;IPathingDaemon&gt;("PATHING_D")
/// </summary>
public sealed class PathingD : DaemonBase, IPathingDaemon
{
    /// <summary>
    /// Daemon identifier - used for lookups.
    /// </summary>
    public override string DaemonId => "PATHING_D";

    public override string Name => "Pathing Daemon";
    public override string Description => "Provides pathfinding for NPC navigation";

    /// <summary>
    /// No periodic updates needed - paths are computed on demand.
    /// Set to 5 minutes just for cache cleanup.
    /// </summary>
    public override TimeSpan HeartbeatInterval => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum search depth to prevent runaway searches in large worlds.
    /// Default: 20 rooms (should cover most local areas).
    /// </summary>
    public int MaxSearchDepth { get; private set; } = 20;

    /// <summary>
    /// Maximum cache entries to prevent memory bloat.
    /// </summary>
    private const int MaxCacheEntries = 1000;

    /// <summary>
    /// Cache of computed paths: (fromId, toId) -> PathResult
    /// </summary>
    private readonly Dictionary<(string, string), CachedPath> _pathCache = new();

    /// <summary>
    /// Cache entry with timestamp for LRU-style eviction.
    /// </summary>
    private readonly struct CachedPath
    {
        public PathResult Result { get; init; }
        public long Timestamp { get; init; }
    }

    protected override void OnInitialize(IMudContext ctx)
    {
        // Load configuration from state if available
        if (ctx.State.Has("max_search_depth"))
        {
            MaxSearchDepth = ctx.State.Get<int>("max_search_depth");
        }
        else
        {
            ctx.State.Set("max_search_depth", MaxSearchDepth);
        }
    }

    protected override void OnHeartbeat(IMudContext ctx)
    {
        // Periodic cache cleanup - remove old entries if cache is too large
        if (_pathCache.Count > MaxCacheEntries)
        {
            CleanupCache();
        }
    }

    /// <summary>
    /// Find a path from one room to another using BFS.
    /// </summary>
    public PathResult FindPath(string fromRoomId, string toRoomId)
    {
        if (Ctx is null)
            return PathResult.NotFound();

        // Normalize room IDs (strip instance numbers for blueprint comparison)
        var fromNorm = NormalizeRoomId(fromRoomId);
        var toNorm = NormalizeRoomId(toRoomId);

        // Already at destination?
        if (string.Equals(fromNorm, toNorm, StringComparison.OrdinalIgnoreCase))
            return PathResult.AlreadyThere;

        // Check cache
        var cacheKey = (fromNorm, toNorm);
        if (_pathCache.TryGetValue(cacheKey, out var cached))
        {
            return cached.Result;
        }

        // Perform BFS
        var result = BreadthFirstSearch(fromNorm, toNorm);

        // Cache the result
        CacheResult(cacheKey, result);

        return result;
    }

    /// <summary>
    /// Get the next direction to move toward a destination.
    /// </summary>
    public string? GetNextDirection(string fromRoomId, string toRoomId)
    {
        var path = FindPath(fromRoomId, toRoomId);
        if (!path.Found || path.Directions.Count == 0)
            return null;

        return path.Directions[0];
    }

    /// <summary>
    /// Check if a path exists between two rooms.
    /// </summary>
    public bool HasPath(string fromRoomId, string toRoomId)
    {
        var path = FindPath(fromRoomId, toRoomId);
        return path.Found;
    }

    /// <summary>
    /// Clear the path cache.
    /// </summary>
    public void ClearCache()
    {
        _pathCache.Clear();
    }

    /// <summary>
    /// BFS implementation for pathfinding.
    /// </summary>
    private PathResult BreadthFirstSearch(string fromRoomId, string toRoomId)
    {
        if (Ctx is null)
            return PathResult.NotFound();

        // Queue entries: (roomId, path taken to get there)
        var queue = new Queue<(string RoomId, List<string> Path)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((fromRoomId, new List<string>()));
        visited.Add(fromRoomId);

        bool truncated = false;

        while (queue.Count > 0)
        {
            var (currentRoomId, currentPath) = queue.Dequeue();

            // Check depth limit
            if (currentPath.Count >= MaxSearchDepth)
            {
                truncated = true;
                continue;
            }

            // Get the room to examine its exits
            var room = GetRoomByBlueprint(currentRoomId);
            if (room is null)
                continue;

            // Check each exit (skip hidden exits - NPCs shouldn't use secret passages)
            foreach (var (direction, targetRoomId) in room.Exits)
            {
                // Skip hidden exits
                if (room.HiddenExits.Contains(direction))
                    continue;

                var targetNorm = NormalizeRoomId(targetRoomId);

                // Found destination?
                if (string.Equals(targetNorm, toRoomId, StringComparison.OrdinalIgnoreCase))
                {
                    var finalPath = new List<string>(currentPath) { direction };
                    return PathResult.Success(finalPath);
                }

                // Skip if already visited
                if (visited.Contains(targetNorm))
                    continue;

                visited.Add(targetNorm);

                // Add to queue with extended path
                var newPath = new List<string>(currentPath) { direction };
                queue.Enqueue((targetNorm, newPath));
            }
        }

        // No path found
        return PathResult.NotFound(truncated);
    }

    /// <summary>
    /// Get a room object by blueprint ID.
    /// Tries to find an existing instance or the blueprint itself.
    /// </summary>
    private IRoom? GetRoomByBlueprint(string blueprintId)
    {
        if (Ctx is null)
            return null;

        // First try to get the exact ID (might be an instance)
        var room = Ctx.World.GetObject<IRoom>(blueprintId);
        if (room is not null)
            return room;

        // Try to find any instance of this blueprint
        foreach (var objId in Ctx.World.ListObjectIds())
        {
            if (objId.StartsWith(blueprintId, StringComparison.OrdinalIgnoreCase))
            {
                room = Ctx.World.GetObject<IRoom>(objId);
                if (room is not null)
                    return room;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalize a room ID by stripping instance numbers.
    /// "Rooms/start.cs#000001" -> "Rooms/start.cs"
    /// </summary>
    private static string NormalizeRoomId(string roomId)
    {
        var hashIndex = roomId.IndexOf('#');
        if (hashIndex > 0)
            return roomId[..hashIndex];
        return roomId;
    }

    /// <summary>
    /// Cache a path result.
    /// </summary>
    private void CacheResult((string, string) key, PathResult result)
    {
        if (Ctx is null)
            return;

        var timestamp = Ctx.World.Now.ToUnixTimeSeconds();
        _pathCache[key] = new CachedPath { Result = result, Timestamp = timestamp };
    }

    /// <summary>
    /// Clean up old cache entries when cache is too large.
    /// Removes the oldest half of entries.
    /// </summary>
    private void CleanupCache()
    {
        if (_pathCache.Count <= MaxCacheEntries / 2)
            return;

        // Find entries to remove (oldest half)
        var entries = new List<((string, string) Key, long Timestamp)>();
        foreach (var kvp in _pathCache)
        {
            entries.Add((kvp.Key, kvp.Value.Timestamp));
        }

        entries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // Remove oldest half
        var removeCount = entries.Count / 2;
        for (int i = 0; i < removeCount; i++)
        {
            _pathCache.Remove(entries[i].Key);
        }
    }

    /// <summary>
    /// Set the maximum search depth (for wizard commands).
    /// </summary>
    public void SetMaxSearchDepth(int depth)
    {
        MaxSearchDepth = Math.Clamp(depth, 5, 100);
        if (Ctx is not null)
        {
            Ctx.State.Set("max_search_depth", MaxSearchDepth);
        }
        ClearCache(); // Clear cache when depth changes
    }
}
