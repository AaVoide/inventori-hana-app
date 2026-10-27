using System.IO;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using InventoryApp.Data;

namespace InventoryApp.Services;

public static class AutomaticBackup
{
    public const int RetentionCount=10;
    public static string Folder=>Path.Combine(AppConfig.BackupDirectory,"automatic");
    public static string RecoveryPasswordFromFolder(string folder)
        =>Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(folder,"recovery.key")),null,DataProtectionScope.CurrentUser));
    public static string LocalRecoveryPassword()
    {
        Directory.CreateDirectory(Folder);
        var path=Path.Combine(Folder,"recovery.key");
        if(File.Exists(path))return RecoveryPasswordFromFolder(Folder);
        if(Directory.EnumerateFiles(Folder,"*.hanaz").Any())throw new InvalidOperationException("Kunci cadangan otomatis hilang. Simpan folder ini dan hubungi Admin; jangan membuat kunci pengganti.");
        var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        file.Write(ProtectedData.Protect(Encoding.UTF8.GetBytes(password),null,DataProtectionScope.CurrentUser));file.Flush(true);return password;
    }
    public static string SnapshotBeforeMigration(SqliteConnection source)
    {
        var path=Path.Combine(Folder,"migration-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..8]+".hanaz");
        BackupBundle.Create(source,path,LocalRecoveryPassword());return path;
    }
    public static string? SaveChangedSession()
    {
        using var source=Db.Open();
        var revision=source.ExecuteScalar<long>("SELECT COALESCE(MAX(id),0) FROM audit_entries WHERE action NOT IN ('LOGIN','BACKUP','EXPORT')");
        revision+=source.ExecuteScalar<long?>("SELECT CAST(setting_value AS INTEGER) FROM app_settings WHERE setting_key='cloud_change_revision'")??0;
        var saved=source.ExecuteScalar<long?>("SELECT CAST(setting_value AS INTEGER) FROM app_settings WHERE setting_key='automatic_backup_revision'")??0;
        if(revision<=saved)return null;
        var password=LocalRecoveryPassword();
        var path=Path.Combine(Folder,"session-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..8]+".hanaz");
        BackupBundle.Create(source,path,password);
        source.Execute("INSERT INTO app_settings(setting_key,setting_value) VALUES('automatic_backup_revision',@revision) ON CONFLICT(setting_key) DO UPDATE SET setting_value=@revision",new {revision=revision.ToString()});
        // Only rotate our session snapshots, after the new snapshot has passed verification.
        foreach(var obsolete in Directory.EnumerateFiles(Folder,"session-*.hanaz").Where(p=>p!=path).OrderByDescending(Path.GetFileName,StringComparer.Ordinal).Skip(RetentionCount-1))File.Delete(obsolete);
        return path;
    }
}
