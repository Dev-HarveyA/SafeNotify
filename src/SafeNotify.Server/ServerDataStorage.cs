using Microsoft.Data.Sqlite;

namespace SafeNotify.Server;

internal static class ServerDataStorage
{
    public static string Resolve(string? configuredDirectory, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory, contentRoot);
        }

        var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SafeNotify", "Server");
        var source = Path.Combine(contentRoot, "data");
        if (!Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(source, "safenotify.db")) && !File.Exists(Path.Combine(destination, "safenotify.db")))
        {
            Migrate(source, destination);
        }

        return destination;
    }

    private static void Migrate(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        var sourceKeys = Path.Combine(source, "keys");
        var destinationKeys = Path.Combine(destination, "keys");
        if (Directory.Exists(sourceKeys))
        {
            Directory.CreateDirectory(destinationKeys);
            foreach (var file in Directory.EnumerateFiles(sourceKeys))
            {
                var target = Path.Combine(destinationKeys, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target);
                }
            }
        }

        var sourceDatabase = Path.Combine(source, "safenotify.db");
        var destinationDatabase = Path.Combine(destination, "safenotify.db");
        var temporaryDatabase = Path.Combine(destination, $"safenotify-{Guid.NewGuid():N}.db");
        var sourceConnectionString = new SqliteConnectionStringBuilder { DataSource = sourceDatabase, Mode = SqliteOpenMode.ReadOnly }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder { DataSource = temporaryDatabase }.ToString();
        using (var sourceConnection = new SqliteConnection(sourceConnectionString))
        using (var destinationConnection = new SqliteConnection(destinationConnectionString))
        {
            sourceConnection.Open();
            destinationConnection.Open();
            sourceConnection.BackupDatabase(destinationConnection);
        }

        File.Move(temporaryDatabase, destinationDatabase);
    }
}
