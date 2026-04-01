using Microsoft.Extensions.Configuration;
using Npgsql;
using System.IO;

namespace BattleServer;

public sealed class BattlePostgresDatabase : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _bootstrapSqlPath;

    public BattlePostgresDatabase(IConfiguration configuration)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("BATTLE_DB_CONNECTION_STRING")
            ?? configuration.GetConnectionString("BattleDatabase")
            ?? "Host=localhost;Port=55432;Database=battle_server;Username=battle_user;Password=battle_password";

        _bootstrapSqlPath =
            Environment.GetEnvironmentVariable("BATTLE_DB_BOOTSTRAP_SQL")
            ?? configuration["BattleDatabase:BootstrapSqlPath"]
            ?? ResolveDefaultBootstrapSqlPath();

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        _dataSource = builder.Build();
    }

    public NpgsqlDataSource DataSource => _dataSource;

    public void EnsureCreated()
    {
        try
        {
            using var connection = _dataSource.OpenConnection();
            if (!ShouldBootstrapFromSql(connection))
                return;

            if (!File.Exists(_bootstrapSqlPath))
                throw new InvalidOperationException(
                    "Bootstrap SQL file not found: " + _bootstrapSqlPath +
                    ". Set env `BATTLE_DB_BOOTSTRAP_SQL` or config `BattleDatabase:BootstrapSqlPath`.");

            string sql = File.ReadAllText(_bootstrapSqlPath);
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException("Bootstrap SQL file is empty: " + _bootstrapSqlPath);

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "28000" || ex.SqlState == "28P01")
        {
            throw new InvalidOperationException(
                "PostgreSQL refused login for the configured user (wrong password or user missing). " +
                "Set env `BATTLE_DB_CONNECTION_STRING` to match your server (Host=...;Port=...;Database=...;Username=...;Password=...), " +
                "or align Postgres user/password with `Server/appsettings.json` / docker-compose (`battle_user` / `battle_password` on port 55432). " +
                "SqlState: " + ex.SqlState,
                ex);
        }
        catch (NpgsqlException ex)
        {
            var inner = ex.InnerException as PostgresException;
            string hint = inner?.SqlState == "28P01"
                ? " Password authentication failed — fix BATTLE_DB_CONNECTION_STRING Password or Postgres role password."
                : "";
            throw new InvalidOperationException(
                "Unable to connect to PostgreSQL." + hint +
                " Start DB with `docker compose up -d` or set `BATTLE_DB_CONNECTION_STRING` / `ConnectionStrings:BattleDatabase`.",
                ex);
        }
    }

    private static bool ShouldBootstrapFromSql(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT to_regclass('public.users') IS NOT NULL;
""";
        bool hasUsersTable = Convert.ToBoolean(cmd.ExecuteScalar() ?? false);
        if (!hasUsersTable)
            return true;

        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*)::INT FROM users;";
        int usersCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        return usersCount <= 0;
    }

    private static string ResolveDefaultBootstrapSqlPath()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "base_alpha1.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "base_alpha1.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "Server", "base_alpha1.sql")
        };
        foreach (string c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        // Return first candidate even if missing so the error message is explicit and stable.
        return candidates[0];
    }

    public void Dispose()
    {
        _dataSource.Dispose();
    }
}
