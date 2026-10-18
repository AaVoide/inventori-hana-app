using System.IO;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;

namespace InventoryApp.Services;

public static class DatabaseVault
{
    public static string? LegacyCopy { get; private set; }
    public static string Prepare(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        var keyPath = path + ".key";
        string key;
        if (File.Exists(keyPath))
        {
            try { key = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { throw new InvalidOperationException("Kunci database milik akun Windows lain atau rusak. Pulihkan cadangan terenkripsi pada folder aplikasi baru."); }
        }
        else
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0 && !IsPlaintext(path))
                throw new InvalidOperationException("Kunci database tidak ditemukan. Pulihkan cadangan; jangan mengganti berkas kunci.");
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var protectedKey = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
            using var keyFile = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            keyFile.Write(protectedKey);
            keyFile.Flush(true);
        }
        if (File.Exists(path) && IsPlaintext(path))
        {
            var temp = path + ".encrypting-" + Guid.NewGuid().ToString("N")[..8];
            using (var source = Open(path, "")) Export(source, temp, key);
            // Keep a verified encrypted snapshot of the old schema for rollback.
            // Never create a second readable copy alongside the encrypted database.
            LegacyCopy = path + ".encrypted-before-0.3-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(temp,LegacyCopy);
            File.Replace(temp, path, null);
        }
        return key;
    }

    private static bool IsPlaintext(string path)
    {
        using var file = File.OpenRead(path);
        var bytes = new byte[16];
        return file.Read(bytes) == 16 && Encoding.ASCII.GetString(bytes) == "SQLite format 3\0";
    }

    public static SqliteConnection Open(string path, string password, bool readOnly = false)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
        }.ToString());
        try
        {
            conn.Open();
            if(password.Length>0)conn.Execute("PRAGMA key='"+password.Replace("'","''")+"'");
            // Validate the key after Open owns its handle, so wrong-password failures
            // can reliably close it before recovery removes its temporary source.
            conn.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master");
            conn.Execute("PRAGMA foreign_keys=ON");
            return conn;
        }
        catch { conn.Dispose(); throw; }
    }

    public static void Export(SqliteConnection source, string destination, string password)
    {
        var version = source.ExecuteScalar<int>("PRAGMA user_version");
        source.Execute("ATTACH DATABASE @destination AS secure_copy KEY @password", new { destination, password });
        try
        {
            source.Execute("SELECT sqlcipher_export('secure_copy')");
            source.Execute($"PRAGMA secure_copy.user_version={version}");
        }
        finally { source.Execute("DETACH DATABASE secure_copy"); }
        using var check = Open(destination, password, true);
        if (check.ExecuteScalar<string>("PRAGMA integrity_check") != "ok" || check.Query("PRAGMA foreign_key_check").Any())
            throw new InvalidOperationException("Cadangan gagal diverifikasi.");
    }
}
