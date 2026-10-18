using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;
using InventoryApp.Data;

namespace InventoryApp.Services;

public static class BackupService
{
    public static void Create(string path,string password)
    {
        AccessControl.RequireAdmin(Session.User.Id);
        if(password.Length<12)throw new ArgumentException("Kata sandi cadangan minimal 12 karakter.");
        var temp=path+".writing-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var source=Db.Open())
            {
                if(Path.GetExtension(path).Equals(".hanaz",StringComparison.OrdinalIgnoreCase))BackupBundle.Create(source,temp,password);
                else DatabaseVault.Export(source,temp,password);
            }
            File.Move(temp,path,true);
            new AuditService().Record("Backup",0,null,"BACKUP",Session.User.Id,note:"Cadangan terenkripsi dibuat dan diverifikasi");
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    // Recovery is available only before the first account is created.
    public static void RestoreFresh(string path,string password)
    {
        if(!new SetupService().NeedsSetup())throw new InvalidOperationException("Pulihkan ke lokasi data baru yang belum memiliki akun. Data aktif tidak ditimpa.");
        using(var current=Db.Open())
        {
            if(current.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name='cloud_cache'")>0 &&
               current.ExecuteScalar<int>("SELECT (SELECT COUNT(*) FROM cloud_cache)+(SELECT COUNT(*) FROM cloud_queue)")>0)
                throw new InvalidOperationException("Cache atau antrean cloud aktif tidak boleh ditimpa. Pulihkan ke lokasi data kosong.");
        }
        var destination=AppConfig.DatabasePath;
        var temp=destination+".restoring-"+Guid.NewGuid().ToString("N");
        var staging=temp+".source";
        try
        {
            // SQLCipher ATTACH inherits a read-only source's mode. Work on a private
            // encrypted copy so export can create its destination without modifying the backup.
            var snapshotPassword=password;
            if(Path.GetExtension(path).Equals(".hanaz",StringComparison.OrdinalIgnoreCase))
            {
                snapshotPassword=Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                BackupBundle.RestoreTo(path,password,staging,snapshotPassword);
            }
            else File.Copy(path,staging);
            using(var source=DatabaseVault.Open(staging,snapshotPassword))
            {
                var localAdmin=source.ExecuteScalar<int>("SELECT COUNT(*) FROM users u JOIN user_access a ON a.user_id=u.id WHERE a.role='ADMIN' AND u.is_active=1")>0;
                var cloudOnly=source.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name='cloud_cache'")>0
                    && source.ExecuteScalar<int>("SELECT (SELECT COUNT(*) FROM users)+(SELECT COUNT(*) FROM assets)")==0;
                if(source.ExecuteScalar<int>("PRAGMA user_version") is <2 or >3 || (!localAdmin&&!cloudOnly))
                    throw new ArgumentException("Cadangan lokal memerlukan Admin aktif; cadangan cache cloud memerlukan login cloud baru setelah pemulihan.");
                DatabaseVault.Export(source,temp,new SqliteConnectionStringBuilder(AppConfig.ConnectionString).Password);
            }
            using(var empty=Db.Open())empty.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
            SqliteConnection.ClearAllPools();
            File.Move(temp,destination,true);
            Db.ResetAfterRestore();Db.EnsureInitialized();
        }
        finally {if(File.Exists(temp))File.Delete(temp);if(File.Exists(staging))File.Delete(staging);}
    }
}
