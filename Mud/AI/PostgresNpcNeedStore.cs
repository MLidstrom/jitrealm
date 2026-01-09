using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace JitRealm.Mud.AI;

internal sealed class PostgresNpcNeedStore : INpcNeedStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresNpcNeedStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertAsync(NpcNeed need, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO npc_needs (npc_id, need_type, level, params, status, updated_at)
VALUES ($1, $2, $3, $4::jsonb, $5, now())
ON CONFLICT (npc_id, need_type) DO UPDATE
SET level = EXCLUDED.level,
    params = EXCLUDED.params,
    status = EXCLUDED.status,
    updated_at = now();
", conn);

        cmd.Parameters.AddWithValue(need.NpcId);
        cmd.Parameters.AddWithValue(need.NeedType);
        cmd.Parameters.AddWithValue(Math.Max(1, need.Level));

        var json = need.Params.RootElement.GetRawText();
        cmd.Parameters.Add(new NpgsqlParameter { Value = json, NpgsqlDbType = NpgsqlDbType.Jsonb });

        cmd.Parameters.AddWithValue(need.Status);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NpcNeed>> GetAllAsync(string npcId, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(@"
SELECT npc_id, need_type, level, params, status, updated_at
FROM npc_needs
WHERE npc_id = $1
ORDER BY level ASC;
", conn);
        cmd.Parameters.AddWithValue(npcId);

        var results = new List<NpcNeed>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new NpcNeed(
                NpcId: reader.GetString(0),
                NeedType: reader.GetString(1),
                Level: reader.GetInt32(2),
                Params: JsonDocument.Parse(reader.GetString(3)),
                Status: reader.GetString(4),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return results;
    }

    public async Task ClearAsync(string npcId, string needType, CancellationToken cancellationToken = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM npc_needs WHERE npc_id = $1 AND need_type = $2;", conn);
        cmd.Parameters.AddWithValue(npcId);
        cmd.Parameters.AddWithValue(needType);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}


