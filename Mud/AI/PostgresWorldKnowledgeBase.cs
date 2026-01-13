using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace JitRealm.Mud.AI;

internal sealed class PostgresWorldKnowledgeBase : IWorldKnowledgeBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _pgvectorEnabled;
    private readonly ILlmService? _llmService;

    public PostgresWorldKnowledgeBase(
        NpgsqlDataSource dataSource,
        bool pgvectorEnabled = false,
        ILlmService? llmService = null)
    {
        _dataSource = dataSource;
        _pgvectorEnabled = pgvectorEnabled;
        _llmService = llmService;
    }

    public async Task UpsertAsync(WorldKbEntry entry, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Auto-generate embedding if pgvector is enabled and no embedding provided
        Vector? embedding = entry.Embedding;
        if (_pgvectorEnabled && embedding is null && _llmService?.IsEmbeddingEnabled == true)
        {
            // Embed summary if provided, otherwise embed JSON value
            var textToEmbed = entry.Summary ?? entry.Value.RootElement.ToString();
            if (!string.IsNullOrWhiteSpace(textToEmbed))
            {
                var floats = await _llmService.EmbedAsync(textToEmbed, cancellationToken);
                if (floats is not null)
                    embedding = new Vector(floats);
            }
        }

        string sql;
        if (_pgvectorEnabled)
        {
            sql = @"
INSERT INTO world_kb (key, value, tags, visibility, npc_ids, summary, embedding, updated_at)
VALUES ($1, $2::jsonb, $3, $4, $5, $6, $7, now())
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value,
    tags = EXCLUDED.tags,
    visibility = EXCLUDED.visibility,
    npc_ids = EXCLUDED.npc_ids,
    summary = EXCLUDED.summary,
    embedding = EXCLUDED.embedding,
    updated_at = now();";
        }
        else
        {
            sql = @"
INSERT INTO world_kb (key, value, tags, visibility, npc_ids, summary, updated_at)
VALUES ($1, $2::jsonb, $3, $4, $5, $6, now())
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value,
    tags = EXCLUDED.tags,
    visibility = EXCLUDED.visibility,
    npc_ids = EXCLUDED.npc_ids,
    summary = EXCLUDED.summary,
    updated_at = now();";
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(entry.Key);
        cmd.Parameters.Add(new NpgsqlParameter { Value = entry.Value.RootElement.GetRawText(), NpgsqlDbType = NpgsqlDbType.Jsonb });
        cmd.Parameters.AddWithValue(entry.Tags.ToArray());
        cmd.Parameters.AddWithValue(entry.Visibility);
        cmd.Parameters.AddWithValue((object?)entry.NpcIds?.ToArray() ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)entry.Summary ?? DBNull.Value);

        if (_pgvectorEnabled)
        {
            cmd.Parameters.AddWithValue((object?)embedding ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorldKbEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
SELECT key, value::text, tags, visibility, updated_at, npc_ids, summary
FROM world_kb
WHERE key = $1;
", conn);
        cmd.Parameters.AddWithValue(key);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadEntry(reader);
    }

    /// <summary>
    /// Original tag-based search (backwards compatible).
    /// </summary>
    public async Task<IReadOnlyList<WorldKbEntry>> SearchByTagsAsync(
        IReadOnlyList<string> tags, int topK, CancellationToken cancellationToken = default)
    {
        return await SearchAsync(new WorldKbSearchQuery(
            QueryText: null,
            Tags: tags,
            NpcId: null,
            TopK: topK
        ), cancellationToken);
    }

    /// <summary>
    /// Advanced search with semantic similarity and NPC filtering.
    /// </summary>
    public async Task<IReadOnlyList<WorldKbEntry>> SearchAsync(
        WorldKbSearchQuery query, CancellationToken cancellationToken = default)
    {
        var topK = Math.Clamp(query.TopK, 0, 50);
        if (topK == 0) return Array.Empty<WorldKbEntry>();

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Build WHERE clause dynamically
        var whereClauses = new List<string> { "visibility IN ('public', 'system', 'npc')" };
        var parameters = new List<NpgsqlParameter>();
        int paramIndex = 1;

        // Tag filtering (array overlap)
        var tags = query.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>();
        if (tags.Length > 0)
        {
            whereClauses.Add($"tags && ${paramIndex}");
            parameters.Add(new NpgsqlParameter { Value = tags });
            paramIndex++;
        }

        // NPC filtering: include entries where npc_ids is NULL (common knowledge) OR contains this NPC
        if (!string.IsNullOrWhiteSpace(query.NpcId))
        {
            whereClauses.Add($"(npc_ids IS NULL OR ${paramIndex} = ANY(npc_ids))");
            parameters.Add(new NpgsqlParameter { Value = query.NpcId });
            paramIndex++;
        }
        else
        {
            // No NPC specified - only return common knowledge
            whereClauses.Add("npc_ids IS NULL");
        }

        var whereClause = string.Join(" AND ", whereClauses);

        // Determine if we can do semantic search
        Vector? queryEmbedding = query.QueryEmbedding;
        if (queryEmbedding is null &&
            _pgvectorEnabled &&
            _llmService?.IsEmbeddingEnabled == true &&
            !string.IsNullOrWhiteSpace(query.QueryText))
        {
            var floats = await _llmService.EmbedAsync(query.QueryText, cancellationToken);
            if (floats is not null)
                queryEmbedding = new Vector(floats);
        }

        string sql;
        if (queryEmbedding is not null && _pgvectorEnabled)
        {
            // Semantic search with vector similarity
            sql = $@"
SELECT key, value::text, tags, visibility, updated_at, npc_ids, summary
FROM world_kb
WHERE {whereClause}
  AND embedding IS NOT NULL
ORDER BY embedding <=> ${paramIndex}
LIMIT {topK};";
            parameters.Add(new NpgsqlParameter { Value = queryEmbedding });
        }
        else
        {
            // Fall back to tag-based search, ordered by recency
            sql = $@"
SELECT key, value::text, tags, visibility, updated_at, npc_ids, summary
FROM world_kb
WHERE {whereClause}
ORDER BY updated_at DESC
LIMIT {topK};";
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var param in parameters)
        {
            cmd.Parameters.Add(param);
        }

        var results = new List<WorldKbEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadEntry(reader));
        }

        return results;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand("DELETE FROM world_kb WHERE key = $1;", conn);
        cmd.Parameters.AddWithValue(key);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static WorldKbEntry ReadEntry(NpgsqlDataReader reader)
    {
        return new WorldKbEntry(
            Key: reader.GetString(0),
            Value: JsonDocument.Parse(reader.GetString(1)),
            Tags: reader.IsDBNull(2) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(2),
            Visibility: reader.GetString(3),
            UpdatedAt: reader.GetFieldValue<DateTimeOffset>(4),
            NpcIds: reader.IsDBNull(5) ? null : reader.GetFieldValue<string[]>(5),
            Summary: reader.IsDBNull(6) ? null : reader.GetString(6)
        );
    }
}
