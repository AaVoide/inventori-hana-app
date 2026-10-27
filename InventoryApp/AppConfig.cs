using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace InventoryApp;

public static class AppConfig
{
    private sealed record Settings(string ConnectionString, string BackupDirectory);
    private static readonly Lazy<Settings> Config = new(Load);
    public static string ConnectionString => Config.Value.ConnectionString;
    public static string BackupDirectory => Config.Value.BackupDirectory;
    public static string DatabasePath => new SqliteConnectionStringBuilder(ConnectionString).DataSource;

    private static Settings Load()
    {
        var connection = "Data Source=inventori-hana.db;";
        var backups = "backups";
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("ConnectionString", out var cs))
                connection = cs.GetString() ?? throw new InvalidOperationException("Lokasi database belum diisi.");
            if (doc.RootElement.TryGetProperty("BackupDirectory", out var bd))
                backups = bd.GetString() ?? "backups";
        }
        var builder = new SqliteConnectionStringBuilder(connection);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
            throw new InvalidOperationException("Gunakan lokasi berkas database yang dapat disimpan.");
        var baseDirectory=AppContext.BaseDirectory;
        // New installations keep their encrypted data outside replaceable release binaries.
        // Existing beside-app databases and explicit absolute paths retain their locations.
        if(!Path.IsPathRooted(builder.DataSource)&&!File.Exists(Path.Combine(baseDirectory,builder.DataSource)))
            baseDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"InventoriHANA","review04");
        builder.DataSource = Path.GetFullPath(builder.DataSource, baseDirectory);
        builder.ForeignKeys = true;
        builder.DefaultTimeout = 10;
        Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        builder.Password = Services.DatabaseVault.Prepare(builder.DataSource);
        return new(builder.ToString(), Path.GetFullPath(backups, Path.GetDirectoryName(builder.DataSource)!));
    }
}
