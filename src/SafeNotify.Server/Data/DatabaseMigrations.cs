using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace SafeNotify.Server.Data;

public static class DatabaseMigrations
{
    private const string InitialMigration = "20261010075818_InitialCreate";
    private const string SpeechMigration = "20261010075825_AddAlertSpeech";
    private const string ProductVersion = "10.0.12";
    private static readonly string[] LegacyTables =
    [
        "Administrators",
        "Alerts",
        "AuditEntries",
        "Deliveries",
        "DeviceGroups",
        "Devices",
        "JoinRequests",
        "Organisations"
    ];

    public static async Task ApplyAsync(SafeNotifyDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await AdoptLegacyDatabaseAsync(db.Database.GetDbConnection());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        await db.Database.MigrateAsync();
    }

    private static async Task AdoptLegacyDatabaseAsync(DbConnection connection)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        if (await TableExistsAsync(connection, transaction, "__EFMigrationsHistory"))
        {
            await transaction.CommitAsync();
            return;
        }

        var existingTables = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingTables.Add(reader.GetString(0));
            }
        }

        if (existingTables.Count == 0)
        {
            await transaction.CommitAsync();
            return;
        }

        if (LegacyTables.Any(table => !existingTables.Contains(table)))
        {
            throw new InvalidOperationException("The SafeNotify database has an incomplete schema and cannot be upgraded safely.");
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, ProductVersion TEXT NOT NULL)";
            await command.ExecuteNonQueryAsync();
        }

        await RecordMigrationAsync(connection, transaction, InitialMigration);
        if (await ColumnExistsAsync(connection, transaction, "Alerts", "SpeakMessage"))
        {
            await RecordMigrationAsync(connection, transaction, SpeechMigration);
        }
        await transaction.CommitAsync();
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, DbTransaction transaction, string table)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table)";
        AddParameter(command, "$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) != 0;
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection connection, DbTransaction transaction, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info('{table}')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task RecordMigrationAsync(DbConnection connection, DbTransaction transaction, string migration)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ($migration, $version)";
        AddParameter(command, "$migration", migration);
        AddParameter(command, "$version", ProductVersion);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
