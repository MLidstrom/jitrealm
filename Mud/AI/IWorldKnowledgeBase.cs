using System.Text.Json;
using Pgvector;

namespace JitRealm.Mud.AI;

public interface IWorldKnowledgeBase
{
    /// <summary>
    /// Insert or update a KB entry. Embedding is auto-generated if null and semantic search is enabled.
    /// </summary>
    Task UpsertAsync(WorldKbEntry entry, CancellationToken cancellationToken = default);

    Task<WorldKbEntry?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search by tags (original method for backwards compatibility).
    /// </summary>
    Task<IReadOnlyList<WorldKbEntry>> SearchByTagsAsync(IReadOnlyList<string> tags, int topK, CancellationToken cancellationToken = default);

    /// <summary>
    /// Semantic search with NPC and tag filtering.
    /// </summary>
    Task<IReadOnlyList<WorldKbEntry>> SearchAsync(WorldKbSearchQuery query, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// A knowledge base entry.
/// </summary>
/// <param name="Key">Unique key for this entry.</param>
/// <param name="Value">JSON value containing the knowledge data.</param>
/// <param name="Tags">Tags for filtering (e.g., "millbrook", "directions", "shopkeeper").</param>
/// <param name="Visibility">Visibility level: "public", "system", "npc".</param>
/// <param name="UpdatedAt">Last update timestamp.</param>
/// <param name="NpcIds">If non-null, only these NPCs can access this entry. NULL = common knowledge.</param>
/// <param name="Summary">Optional text summary for embedding (embeds this instead of full JSON).</param>
/// <param name="Embedding">Pre-computed embedding vector (optional, auto-generated on upsert if null).</param>
public sealed record WorldKbEntry(
    string Key,
    JsonDocument Value,
    IReadOnlyList<string> Tags,
    string Visibility,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string>? NpcIds = null,
    string? Summary = null,
    Vector? Embedding = null);

/// <summary>
/// Query parameters for KB search.
/// </summary>
/// <param name="QueryText">Text to search semantically (embedded and compared to KB entries).</param>
/// <param name="Tags">Tags to filter by (array overlap).</param>
/// <param name="NpcId">If provided, includes entries where npc_ids is NULL OR contains this NPC.</param>
/// <param name="TopK">Maximum results to return.</param>
/// <param name="QueryEmbedding">Pre-computed query embedding (optional, computed from QueryText if null).</param>
public sealed record WorldKbSearchQuery(
    string? QueryText,
    IReadOnlyList<string>? Tags,
    string? NpcId,
    int TopK = 5,
    Vector? QueryEmbedding = null);
