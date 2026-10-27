using System.IO;
using Dapper;
using InventoryApp;
using InventoryApp.Data;
using InventoryApp.Models;
using InventoryApp.Services;

internal static class V04LocalChecks
{
    public static void Run(User admin,int item,int building,string root)
    {
        using var c=Db.Open();var service=new AssetService();var accounts=new AccountService();
        Program.Check(c.ExecuteScalar<int>("PRAGMA user_version")==3,"v0.4 local schema installed without renumbering assets");
        var before=c.ExecuteScalar<int>("SELECT COUNT(*) FROM assets");
        Program.Reject(()=>service.CreateAssets(item,1,null,null,null,null,admin.Id,building),"Similar manual asset requires deliberate override");
        Program.Check(c.ExecuteScalar<int>("SELECT COUNT(*) FROM assets")==before,"Duplicate warning makes no partial write");
        accounts.Create("editorcheck","Editor Pengujian","Editor-test-password-2026","EDITOR");
        Session.CurrentUser=new AuthService().Login("editorcheck","Editor-test-password-2026");
        var editor=Session.User;
        service.CreateAssets(item,1,null,null,null,null,editor.Id,building,true);
        Program.Check(c.ExecuteScalar<int>("SELECT COUNT(*) FROM audit_entries WHERE action='DUPLICATE_OVERRIDE' AND user_id=@id",new{id=editor.Id})==1,"Editor duplicate override audited in creation transaction");
        Program.Reject(()=>accounts.SetAccess(editor.Id,"ADMIN",true),"Editor cannot promote self to Admin");
        Program.Reject(()=>new MasterDataService().Change(MasterKind.Building,building,"DELETE"),"Editor cannot delete reference data");
        Session.CurrentUser=admin;
        var archive=Path.Combine(root,"compressed.hanaz");const string password="Test-only-password-2026";
        BackupService.Create(archive,password);
        Program.Child("--restore",Path.Combine(root,"restore-compressed","inventory.db"),archive,(before+1).ToString());
        Program.Check(File.ReadAllBytes(archive).Take(8).SequenceEqual(System.Text.Encoding.ASCII.GetBytes("HANA040\n")),"Compressed backup has versioned authenticated format");
        Program.Reject(()=>BackupBundle.Verify(archive,"Incorrect-backup-password"),"Wrong compressed-backup password rejected");
        var corrupt=Path.Combine(root,"corrupt.hanaz");var bytes=File.ReadAllBytes(archive);bytes[^1]^=1;File.WriteAllBytes(corrupt,bytes);
        Program.Reject(()=>BackupBundle.Verify(corrupt,password),"Modified backup ciphertext rejected");
        var restored=Path.Combine(root,"bundle-restored.db");BackupBundle.RestoreTo(archive,password,restored,"isolated-restore-key");
        using(var restoredDb=DatabaseVault.Open(restored,"isolated-restore-key",true))
        {
            Program.Check(restoredDb.ExecuteScalar<int>("SELECT COUNT(*) FROM assets")==before+1,"Compressed backup restores all asset records");
            Program.Check(restoredDb.ExecuteScalar<int>("SELECT COUNT(*) FROM audit_entries WHERE action='DUPLICATE_OVERRIDE'")==c.ExecuteScalar<int>("SELECT COUNT(*) FROM audit_entries WHERE action='DUPLICATE_OVERRIDE'"),"Compressed restore retains override audit history");
        }
        Program.Reject(()=>BackupBundle.RestoreTo(archive,password,restored,"isolated-restore-key"),"Restore cannot overwrite existing destination");
        Program.Check(AutomaticBackup.SaveChangedSession() is not null,"Changed session creates verified automatic backup");
        Program.Check(AutomaticBackup.SaveChangedSession() is null,"Unchanged session avoids repeated backup");
        for(var i=0;i<11;i++)
        {
            new AuditService().Record("Check",i,null,"UPDATE",admin.Id,note:"Synthetic retention check");
            AutomaticBackup.SaveChangedSession();
        }
        var snapshots=Directory.GetFiles(AutomaticBackup.Folder,"session-*.hanaz");
        Program.Check(snapshots.Length==AutomaticBackup.RetentionCount,"Automatic retention rotates to ten verified snapshots");
        BackupBundle.Verify(snapshots.OrderBy(x=>x).Last(),AutomaticBackup.LocalRecoveryPassword());
        Program.Check(true,"Rotated automatic backup remains decryptable and valid");
    }
}
