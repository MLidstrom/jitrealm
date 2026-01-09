using Npgsql;
using Pgvector;
using NpgsqlTypes;

namespace JitRealm.Mud.AI;

internal sealed class PostgresNpcMemoryStore : INpcMemoryStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _candidateLimitDefault;
    private readonly bool _pgvectorEnabled;

    public PostgresNpcMemoryStore(NpgsqlDataSource dataSource, JitRealm.Mud.Configuration.MemorySettings settings, bool pgvectorEnabled)
    {
        _dataSource = dataSource;
        _candidateLimitDefault = Math.Clamp(settings.CandidateLimit, 10, 5000);
        _pgvectorEnabled = pgvectorEnabled;
    }

    public async Task AddAsync(NpcMemoryWrite write, CancellationToken cancellationToken = default)
    {
        if (write.Id == Guid.Empty)
            throw new ArgumentException("Memory id must be set", nameof(write));
        if (string.IsNullOrWhiteSpace(write.NpcId))
            throw new ArgumentException("NpcId must be set", nameof(write));

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Insert with or without embedding depending on whether pgvector is enabled.
        var sql = _pgvectorEnabled
            ? @"
INSERT INTO npc_memories
  (id, npc_id, subject_player, room_id, area_id, kind, importance, tags, content, expires_at, embedding)
VALUES
  ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11);"
            : @"
INSERT INTO npc_memories
  (id, npc_id, subject_player, room_id, area_id, kind, importance, tags, content, expires_at)
VALUES
  ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10);";

        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.AddWithValue(write.Id);
        cmd.Parameters.AddWithValue(write.NpcId);
        cmd.Parameters.AddWithValue((object?)NormalizePlayer(write.SubjectPlayer) ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)write.RoomId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)write.AreaId ?? DBNull.Value);
        cmd.Parameters.AddWithValue(write.Kind);
        cmd.Parameters.AddWithValue(Math.Clamp(write.Importance, 0, 100));
        cmd.Parameters.AddWithValue(write.Tags.ToArray());
        cmd.Parameters.AddWithValue(write.Content);
        cmd.Parameters.AddWithValue((object?)write.ExpiresAt ?? DBNull.Value);

        if (_pgvectorEnabled)
        {
            cmd.Parameters.AddWithValue((object?)write.Embedding ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NpcMemory>> RecallAsync(NpcMemoryRecallQuery query, CancellationToken cancellationToken = default)
    {
        var topK = Math.Clamp(query.TopK <= 0 ? 10 : query.TopK, 0, 50);
        if (topK == 0)
            return Array.Empty<NpcMemory>();

        var candidateLimit = Math.Clamp(query.CandidateLimit <= 0 ? _candidateLimitDefault : query.CandidateLimit, 10, 5000);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Two-stage retrieval:
        // 1) Candidates by recency (and optional subject), filtered by TTL
        // 2) Rerank by embedding similarity if available, otherwise by importance/recency
        var hasVectorQuery = _pgvectorEnabled && query.QueryEmbedding is not null;
        var subject = NormalizePlayer(query.SubjectPlayer);

        var baseWhere = @"
npc_id = @npc_id
AND (expires_at IS NULL OR expires_at > now())
";
        if (!string.IsNullOrEmpty(subject))
            baseWhere += "AND subject_player = @subject\n";

        // tag filtering is optional; applied as overlap (tags && @tags)
        var tags = query.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray() ?? Array.Empty<string>();
        var hasTags = tags.Length > 0;
        if (hasTags)
            baseWhere += "AND tags && @tags\n";

        var sql = hasVectorQuery
            ? $@"
WITH candidates AS (
  SELECT id, npc_id, subject_player, room_id, area_id, kind, importance, tags, content, created_at, expires_at, embedding
  FROM npc_memories
  WHERE {baseWhere}
  ORDER BY created_at DESC
  LIMIT {candidateLimit}
)
SELECT id, npc_id, subject_player, room_id, area_id, kind, importance, tags, content, created_at, expires_at
FROM candidates
WHERE embedding IS NOT NULL
ORDER BY embedding <=> @embedding
LIMIT {topK};
"
            : $@"
WITH candidates AS (
  SELECT id, npc_id, subject_player, room_id, area_id, kind, importance, tags, content, created_at, expires_at
  FROM npc_memories
  WHERE {baseWhere}
  ORDER BY created_at DESC
  LIMIT {candidateLimit}
)
SELECT id, npc_id, subject_player, room_id, area_id, kind, importance, tags, content, created_at, expires_at
FROM candidates
ORDER BY importance DESC, created_at DESC
LIMIT {topK};
";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("npc_id", query.NpcId);

        if (!string.IsNullOrEmpty(subject))
        {
            cmd.Parameters.AddWithValue("subject", subject);
        }

        if (hasTags)
        {
            cmd.Parameters.AddWithValue("tags", tags);
        }

        if (hasVectorQuery)
        {
            cmd.Parameters.AddWithValue("embedding", query.QueryEmbedding!);
        }

        var results = new List<NpcMemory>(topK);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new NpcMemory(
                Id: reader.GetGuid(0),
                NpcId: reader.GetString(1),
                SubjectPlayer: reader.IsDBNull(2) ? null : reader.GetString(2),
                RoomId: reader.IsDBNull(3) ? null : reader.GetString(3),
                AreaId: reader.IsDBNull(4) ? null : reader.GetString(4),
                Kind: reader.GetString(5),
                Importance: reader.GetInt32(6),
                Tags: reader.IsDBNull(7) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(7),
                Content: reader.GetString(8),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(9),
                ExpiresAt: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)));
        }

        return results;
    }

    private static string? NormalizePlayer(string? playerName) =>
        string.IsNullOrWhiteSpace(playerName) ? null : playerName.Trim().ToLowerInvariant();
}


