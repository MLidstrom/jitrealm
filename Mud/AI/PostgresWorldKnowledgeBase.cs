using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace JitRealm.Mud.AI;

internal sealed class PostgresWorldKnowledgeBase : IWorldKnowledgeBase
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresWorldKnowledgeBase(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertAsync(WorldKbEntry entry, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO world_kb (key, value, tags, visibility, updated_at)
VALUES ($1, $2::jsonb, $3, $4, now())
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value,
    tags = EXCLUDED.tags,
    visibility = EXCLUDED.visibility,
    updated_at = now();
", conn);

        cmd.Parameters.AddWithValue(entry.Key);
        cmd.Parameters.Add(new NpgsqlParameter { Value = entry.Value.RootElement.GetRawText(), NpgsqlDbType = NpgsqlDbType.Jsonb });
        cmd.Parameters.AddWithValue(entry.Tags.ToArray());
        cmd.Parameters.AddWithValue(entry.Visibility);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorldKbEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
SELECT key, value::text, tags, visibility, updated_at
FROM world_kb
WHERE key = $1;
", conn);
        cmd.Parameters.AddWithValue(key);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var k = reader.GetString(0);
        var valueJson = reader.GetString(1);
        var tags = reader.IsDBNull(2) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(2);
        var vis = reader.GetString(3);
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(4);

        return new WorldKbEntry(
            Key: k,
            Value: JsonDocument.Parse(valueJson),
            Tags: tags,
            Visibility: vis,
            UpdatedAt: updatedAt);
    }

    public async Task<IReadOnlyList<WorldKbEntry>> SearchByTagsAsync(IReadOnlyList<string> tags, int topK, CancellationToken cancellationToken = default)
    {
        topK = Math.Clamp(topK, 0, 50);
        if (topK == 0) return Array.Empty<WorldKbEntry>();

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
SELECT key, value::text, tags, visibility, updated_at
FROM world_kb
WHERE visibility IN ('public', 'system')
  AND tags && $1
LIMIT $2;
", conn);
        cmd.Parameters.AddWithValue(tags.ToArray());
        cmd.Parameters.AddWithValue(topK);

        var results = new List<WorldKbEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new WorldKbEntry(
                Key: reader.GetString(0),
                Value: JsonDocument.Parse(reader.GetString(1)),
                Tags: reader.IsDBNull(2) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(2),
                Visibility: reader.GetString(3),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(4)));
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
}
