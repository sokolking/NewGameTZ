using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BattleServer;

public sealed record DatabaseBackupImportResult(IReadOnlyDictionary<string, int> RowCounts);

/// <summary>
/// Full logical backup of all application tables (PostgreSQL) as JSON.
/// </summary>
public static class BattleDatabaseBackup
{
    public const int SchemaVersion = 1;

    public const long MaxBackupImportBodyBytes = 200L * 1024 * 1024;

    /// <summary>
    /// If <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> key <c>DatabaseBackup:Secret</c> is set,
    /// clients must send the same value in header <c>X-Database-Backup-Key</c> or query <c>key</c>.
    /// </summary>
    public static bool BackupAuthorizationOk(IConfiguration cfg, HttpRequest req)
    {
        string? secret = cfg["DatabaseBackup:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            return true;
        if (req.Headers.TryGetValue("X-Database-Backup-Key", out var h) && h.Count > 0 && h[0] == secret)
            return true;
        if (req.Query.TryGetValue("key", out var q) && q.Count > 0 && q[0] == secret)
            return true;
        return false;
    }

    /// <summary>
    /// Logs Host/Database/Username (no password) so operators can confirm import hits the expected Postgres instance.
    /// </summary>
    public static void LogDatabaseTarget(IConfiguration cfg)
    {
        try
        {
            string? cs = Environment.GetEnvironmentVariable("BATTLE_DB_CONNECTION_STRING")
                ?? cfg.GetConnectionString("BattleDatabase");
            if (string.IsNullOrWhiteSpace(cs))
            {
                Console.WriteLine("[Backup] Warning: no connection string (BattleDatabase / BATTLE_DB_CONNECTION_STRING).");
                return;
            }

            var b = new NpgsqlConnectionStringBuilder(cs);
            Console.WriteLine($"[Backup] Target DB Host={b.Host};Port={b.Port};Database={b.Database};Username={b.Username}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Backup] Could not parse connection string for logging: " + ex.Message);
        }
    }

    /// <summary>
    /// FK-safe insert order (after truncate).
    /// </summary>
    private static readonly string[] TableOrder =
    {
        "body_parts",
        "battle_obstacle_balance",
        "battle_zone_shrink",
        "hope_schema_migrations",
        "users",
        "items",
        "weapons",
        "medicine",
        "battles",
        "battle_turns",
        "battle_turn_links",
        "user_inventory_items",
    };

    private const string TruncateSql = """
        TRUNCATE TABLE
            user_inventory_items,
            battle_turn_links,
            battle_turns,
            battles,
            medicine,
            weapons,
            items,
            users,
            battle_zone_shrink,
            battle_obstacle_balance,
            body_parts,
            hope_schema_migrations
        RESTART IDENTITY CASCADE;
        """;

    private static readonly (string Table, string Column)[] SerialColumns =
    {
        ("users", "id"),
        ("items", "id"),
        ("weapons", "id"),
        ("user_inventory_items", "id"),
    };

    public static async Task<string> ExportJsonAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        var tablesObj = new JsonObject();
        foreach (string table in TableOrder)
        {
            string sql = BuildExportSql(table);
            await using var cmd = new NpgsqlCommand(sql, connection);
            object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
            string jsonArray = scalar as string ?? "[]";
            tablesObj[table] = JsonNode.Parse(jsonArray)!;
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["exportedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["tables"] = tablesObj
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildExportSql(string table)
    {
        string order = table switch
        {
            "battles" => "battle_id",
            "battle_turns" => "turn_id",
            "battle_turn_links" => "battle_id, turn_index",
            "hope_schema_migrations" => "id",
            _ => "id"
        };

        return $"""
            SELECT coalesce(json_agg(row_to_json(t)), '[]'::json)::text
            FROM (SELECT * FROM {QuoteIdent(table)} ORDER BY {order}) t;
            """;
    }

    public static async Task<DatabaseBackupImportResult> ImportJsonAsync(
        NpgsqlConnection connection,
        string json,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("schemaVersion", out JsonElement verEl) || verEl.GetInt32() != SchemaVersion)
            throw new InvalidOperationException($"Unsupported or missing schemaVersion (expected {SchemaVersion}).");

        if (!root.TryGetProperty("tables", out JsonElement tablesEl))
            throw new InvalidOperationException("Missing \"tables\" object.");

        var rowCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var trunc = new NpgsqlCommand(TruncateSql, connection, tx))
                await trunc.ExecuteNonQueryAsync(cancellationToken);

            foreach (string table in TableOrder)
            {
                if (!tablesEl.TryGetProperty(table, out JsonElement arrEl))
                {
                    rowCounts[table] = 0;
                    continue;
                }

                if (arrEl.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException($"Table \"{table}\" must be a JSON array (possibly empty).");

                int n = arrEl.GetArrayLength();
                rowCounts[table] = n;

                if (n == 0)
                    continue;

                string payload = arrEl.GetRawText();
                string insertSql = BuildInsertFromJsonSql(table);
                await using var insertCmd = new NpgsqlCommand(insertSql, connection, tx);
                insertCmd.Parameters.AddWithValue("payload", NpgsqlTypes.NpgsqlDbType.Json, payload);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await ResetSequencesAsync(connection, tx, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return new DatabaseBackupImportResult(rowCounts);
    }

    private static string BuildInsertFromJsonSql(string table)
    {
        string t = QuoteIdent(table);
        return $"""
            INSERT INTO {t}
            SELECT * FROM json_populate_recordset(NULL::{t}, @payload::json);
            """;
    }

    private static async Task ResetSequencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CancellationToken cancellationToken)
    {
        foreach ((string table, string column) in SerialColumns)
        {
            await using (var seqQ = new NpgsqlCommand(
                             $"SELECT pg_get_serial_sequence('{table}', '{column}');",
                             connection,
                             tx))
            {
                object? seq = await seqQ.ExecuteScalarAsync(cancellationToken);
                if (seq is not string seqName || string.IsNullOrEmpty(seqName))
                    continue;

                string setSql = $"""
                    SELECT setval($1::regclass, COALESCE((SELECT MAX("{column}") FROM {QuoteIdent(table)}), 1), true);
                    """;
                await using var setCmd = new NpgsqlCommand(setSql, connection, tx);
                setCmd.Parameters.AddWithValue(seqName);
                await setCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static string QuoteIdent(string name)
    {
        if (string.IsNullOrEmpty(name) || name.AsSpan().ContainsAny(stackalloc char[] { ';', ' ', '"', '\'', '\\' }))
            throw new ArgumentException("Invalid table name.", nameof(name));
        return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
