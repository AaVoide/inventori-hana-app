using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dapper;
using Microsoft.Data.Sqlite;
using ClosedXML.Excel;
using InventoryApp;
using InventoryApp.Data;
using InventoryApp.Models;
using InventoryApp.Services;
using InventoryApp.Views;

internal static class Program
{
    private static int _checks;
    private const string Password="Test-only-password-2026";
    internal static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);_checks++;}
    internal static void Reject(Action action,string name)
    {
        try{action();}catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or SqliteException or UnauthorizedAccessException or IOException or InvalidDataException){Check(true,name);return;}
        throw new Exception("FAIL (unexpected success): "+name);
    }
    private static void Configure(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"appsettings.json"),JsonSerializer.Serialize(new{ConnectionString="Data Source="+path,BackupDirectory=Path.Combine(Path.GetDirectoryName(path)!,"backups")}));
    }
    internal static void Child(params string[] args)
    {
        var info=new System.Diagnostics.ProcessStartInfo("C:\\Program Files\\dotnet\\dotnet.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(typeof(Program).Assembly.Location);foreach(var arg in args)info.ArgumentList.Add(arg);
        using var child=System.Diagnostics.Process.Start(info)!;
        var output=child.StandardOutput.ReadToEnd();var error=child.StandardError.ReadToEnd();child.WaitForExit();Console.Write(output);Console.Write(error);
        Check(child.ExitCode==0,"Fresh-process "+args[0]);
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if(args.FirstOrDefault()=="--cloud-live")
            {
                Configure(Path.GetFullPath(args[1]));
                CloudLiveChecks.Run().GetAwaiter().GetResult();
                Console.WriteLine($"SUCCESS: {_checks} live HTTP cloud checks.");return 0;
            }
            if(args.FirstOrDefault()=="--preview-cloud")
            {
                Configure(args[1]);var previewApp=new App();previewApp.InitializeComponent();previewApp.ShutdownMode=ShutdownMode.OnExplicitShutdown;
                Render(PreviewCloudInventory(),args[2]);return 0;
            }
            if(args.FirstOrDefault()=="--cloud-only")
            {
                Configure(args[1]);var store=new CloudStore();
                store.Queue(Guid.NewGuid(),new(){Id=Guid.NewGuid(),Data=new(){Code="CLOUD-ONLY",Name="Recovery asset",Building="Recovery building"}},false);
                var bundle=args[1]+".hanaz";using(var source=Db.Open())BackupBundle.Create(source,bundle,Password);
                Child("--restore-cloud",args[1]+".restored.db",bundle);return 0;
            }
            if(args.FirstOrDefault()=="--restore-cloud")
            {
                Configure(args[1]);BackupService.RestoreFresh(args[2],Password);
                using var source=Db.Open();
                Check(source.ExecuteScalar<int>("SELECT COUNT(*) FROM users")==0,"Cloud-only restore does not invent local credentials");
                Check(source.ExecuteScalar<int>("SELECT COUNT(*) FROM cloud_queue")==1,"Fresh cloud-only restore preserves queued work");
                Reject(()=>BackupService.RestoreFresh(args[2],Password),"Cloud cache without local accounts still cannot be overwritten");return 0;
            }
            if(args.FirstOrDefault()=="--restore")
            {
                Configure(args[1]);Check(new SetupService().NeedsSetup(),"Recovery starts with empty installation");
                Reject(()=>BackupService.RestoreFresh(args[2],"incorrect-password"),"Wrong backup password rejected");
                Check(new SetupService().NeedsSetup(),"Failed recovery leaves fresh database intact");
                BackupService.RestoreFresh(args[2],Password);
                using var c=Db.Open();Check(c.ExecuteScalar<int>("SELECT COUNT(*) FROM assets")==int.Parse(args[3]),"Recovery restores all assets");
                Check(new AuthService().Login("admincheck",Password)?.Role=="ADMIN","Recovery preserves account login");
                Check(c.ExecuteScalar<string>("PRAGMA integrity_check")=="ok","Recovered database passes integrity check");
                return 0;
            }
            if(args.FirstOrDefault()=="--upgrade")
            {
                Configure(args[1]);using var c=Db.Open();Check(c.ExecuteScalar<int>("PRAGMA user_version")==3,"Legacy schema migrated");
                Check(c.ExecuteScalar<int>("SELECT COUNT(*) FROM assets")==int.Parse(args[2]),"Migration preserves assets");
                Check(c.ExecuteScalar<int>("SELECT COUNT(*) FROM user_access WHERE role='REVIEW'")>0,"Legacy staff requires explicit role review");
                Check(DatabaseVault.LegacyCopy is not null && File.Exists(DatabaseVault.LegacyCopy),"Encrypted rollback snapshot retained and reported");
                Reject(()=>{using var copy=DatabaseVault.Open(DatabaseVault.LegacyCopy!,"",true);},"Rollback snapshot cannot be read without key");
                using var raw=new FileStream(args[1],FileMode.Open,FileAccess.Read,FileShare.ReadWrite);var header=new byte[16];raw.ReadExactly(header);
                Check(!System.Text.Encoding.ASCII.GetString(header).StartsWith("SQLite"),"Migrated live database is encrypted");
                return 0;
            }
            var root=Path.GetFullPath("../../../../.check-data/"+Guid.NewGuid().ToString("N")[..8],AppContext.BaseDirectory);
            Directory.CreateDirectory(root);Configure(Path.Combine(root,"inventory.db"));
            var setup=new SetupService();Check(setup.NeedsSetup(),"First run requests admin setup");
            Check(File.Exists(AppConfig.DatabasePath+".key"),"Windows-protected encryption key stored separately");
            using var conn=Db.Open();
            Check(!string.IsNullOrEmpty(conn.ExecuteScalar<string>("PRAGMA cipher_version")),"SQLCipher encryption provider loaded");
            Check(conn.ExecuteScalar<int>("SELECT COUNT(*) FROM assets")==0,"Fresh inventory has no sample assets");
            Check(conn.ExecuteScalar<int>("PRAGMA foreign_keys")==1,"Foreign keys enabled");
            Reject(()=>setup.CreateAdministrator("a","Test","short"),"Weak setup credentials rejected");
            setup.CreateAdministrator("admincheck","Admin Pengujian",Password);
            Reject(()=>setup.CreateAdministrator("other","Other",Password),"Setup cannot overwrite accounts");
            var auth=new AuthService();Check(auth.Login("admincheck","wrong") is null,"Wrong login rejected");
            Session.CurrentUser=auth.Login("admincheck",Password);var admin=Session.User;
            var masters=new MasterDataService();foreach(var kind in new[]{MasterKind.Category,MasterKind.Building,MasterKind.Room,MasterKind.ItemType})Check(masters.List(kind).Count==0,"Empty reference list: "+kind);
            var category=(int)masters.Create(MasterKind.Category,"Mebel",null,null,admin.Id);
            var building=(int)masters.Create(MasterKind.Building,"Gedung A",null,null,admin.Id);
            var building2=(int)masters.Create(MasterKind.Building,"Gedung B",null,null,admin.Id);
            var room=(int)masters.Create(MasterKind.Room,"Ruang 101",null,building,admin.Id);
            var item=(int)masters.Create(MasterKind.ItemType,"Kursi","KRS",category,admin.Id);
            Reject(()=>masters.Create(MasterKind.Category," mebel ",null,null,admin.Id),"Duplicate reference names rejected");
            var service=new AssetService();var repository=new AssetRepository();
            Reject(()=>service.CreateAssets(item,1,null,null,null,null,admin.Id),"Building is mandatory");
            Reject(()=>service.CreateAssets(item,1,room,null,null,null,admin.Id,building2),"Room must belong to building");
            var codes=service.CreateAssets(item,23,null,null,null,"Catatan awal",admin.Id,building);
            Check(codes.Count==23 && codes.Distinct().Count()==23,"Room optional; every physical asset receives a unique code");
            Check(conn.ExecuteScalar<int>("SELECT COUNT(*) FROM identifiers")==23,"QR identifiers prepared for all assets");
            var asset=repository.FindByIdentifier(codes[0])!;
            var change=new AssetChangeSet{BuildingId=building,ExpectedVersion=asset.RowVersion,Notes="Catatan awal"};
            Check(service.UpdateAsset(asset.Id,change,admin.Id)==0,"Unchanged save does not create history");
            change.Notes="Catatan baru";Check(service.UpdateAsset(asset.Id,change,admin.Id)==1,"Edit creates one audited change");
            Reject(()=>service.UpdateAsset(asset.Id,change,admin.Id),"Stale edit rejected");
            Reject(()=>conn.Execute("UPDATE assets SET asset_code='CHANGED' WHERE id=@id",new{id=asset.Id}),"Asset codes immutable");
            Reject(()=>conn.Execute("UPDATE audit_entries SET note='changed'"),"Audit cannot be edited");
            Reject(()=>conn.Execute("DELETE FROM audit_entries"),"Audit cannot be deleted");
            Reject(()=>masters.Change(MasterKind.Building,building,"DELETE"),"Used reference cannot be deleted");
            var unused=masters.Create(MasterKind.Building,"Typo",null,null,admin.Id);masters.Change(MasterKind.Building,unused,"DELETE");
            Check(!masters.List(MasterKind.Building).Any(r=>r.Id==unused),"Unused reference deleted");
            masters.Change(MasterKind.Building,building2,"RENAME","Gedung Barat");Check(masters.List(MasterKind.Building).Any(r=>r.Name=="Gedung Barat"),"Reference renamed");
            Reject(()=>masters.Change(MasterKind.Building,building2,"RENAME","gedung a"),"Duplicate rename rejected");
            masters.Change(MasterKind.Building,building,"DISABLE");
            Reject(()=>masters.Create(MasterKind.Room,"Blocked",null,building,admin.Id),"Inactive parent rejects new references");
            change.ExpectedVersion=1;change.Notes="Catatan tetap";Check(service.UpdateAsset(asset.Id,change,admin.Id)==1,"Existing inactive location retained during unrelated edit");
            masters.Change(MasterKind.Building,building,"ENABLE");
            var before=repository.Count(null,null,null);
            conn.Execute("CREATE TRIGGER test_fail_second BEFORE INSERT ON audit_entries WHEN NEW.entity_code='KRS-0025' BEGIN SELECT RAISE(ABORT,'Simulated failure'); END;");
            Reject(()=>service.CreateAssets(item,2,null,null,null,null,admin.Id,building,true),"Mid-batch write failure handled");conn.Execute("DROP TRIGGER test_fail_second");
            Check(repository.Count(null,null,null)==before,"Failed batch rolls back every asset");
            var accounts=new AccountService();accounts.Create("viewercheck","Viewer Pengujian",Password,"VIEWER");
            var viewer=accounts.List().Single(u=>u.Username=="viewercheck");
            Reject(()=>accounts.SetAccess(admin.Id,"VIEWER",true),"Current admin cannot demote self");
            Session.CurrentUser=auth.Login("viewercheck",Password);
            Reject(()=>service.CreateAssets(item,1,null,null,null,null,viewer.Id,building),"Viewer cannot create assets");
            Reject(()=>service.UpdateAsset(asset.Id,change,viewer.Id),"Viewer cannot edit assets");
            Reject(()=>masters.Create(MasterKind.Building,"Forbidden",null,null,viewer.Id),"Viewer cannot manage references");
            Reject(()=>accounts.Create("blocked","Blocked",Password,"ADMIN"),"Viewer cannot add accounts");
            Reject(()=>SpreadsheetService.Export(Path.Combine(root,"forbidden.csv")),"Viewer cannot export inventory");
            Reject(()=>BackupService.Create(Path.Combine(root,"forbidden.hana"),Password),"Viewer cannot create backup");
            Check(repository.Count(null,null,null)==23,"Viewer can browse assets");
            accounts.ChangePassword(viewer.Id,Password,"New-test-password-2026");Check(auth.Login("viewercheck","New-test-password-2026") is not null,"Viewer can change own password");
            Session.CurrentUser=admin;accounts.SetAccess(viewer.Id,"VIEWER",false);Check(auth.Login("viewercheck","New-test-password-2026") is null,"Inactive account cannot log in");accounts.SetAccess(viewer.Id,"VIEWER",true);
            var csv=Path.Combine(root,"input.csv");File.WriteAllText(csv,"Nama Barang,Tipe Group,Gedung,Ruangan Barang,Jumlah Barang,Tahun Perolehan,Keterangan\n\"Meja, lipat\",Mebel,Gedung A,,2,2020,\"Baris satu\nbaris dua\"\n");
            var data=SpreadsheetService.Read(csv,"CSV");var rows=SpreadsheetService.Map(data,SpreadsheetService.Fields.Select(f=>SpreadsheetService.Guess(f,data.Headers)).ToArray());
            Check(rows.Single().Name=="Meja, lipat" && rows[0].Notes.Contains('\n'),"CSV quoted commas and newlines preserved");
            Check(!SpreadsheetService.Validate(rows,false,false),"Unknown references require explicit opt-in");
            Check(SpreadsheetService.Validate(rows,true,false),"Valid new references can be approved");
            Check(repository.Count(null,null,null)==23,"Import preview makes no database writes");
            Check(SpreadsheetService.Import(rows,"input.csv",SpreadsheetService.Hash(csv),true,false)==2,"Quantity expands into individual imported assets");
            Reject(()=>SpreadsheetService.Import(rows,"input.csv",SpreadsheetService.Hash(csv),true,true),"Same source file cannot be imported twice");
            rows[0].Building="";Check(!SpreadsheetService.Validate(rows,true,true),"Missing imported building flagged");rows[0].Building="Gedung A";
            rows[0].Quantity="2.5";Check(!SpreadsheetService.Validate(rows,true,true),"Fractional quantity flagged");rows[0].Quantity="1";
            var conflicting=new List<ImportRow>{new(){Name="Different",Category="Mebel",Building="Gedung A",Color="Red"},new(){Name="Different",Category="Mebel",Building="Gedung A",Color="Blue"}};
            Check(!SpreadsheetService.Validate(conflicting,true,true),"Conflicting item descriptions do not silently merge");
            conn.Execute("CREATE TRIGGER fail_import BEFORE INSERT ON assets WHEN NEW.asset_code='AST-0004' BEGIN SELECT RAISE(ABORT,'Simulated import failure'); END;");
            rows[0].Name="Rollback item";rows[0].Quantity="2";Reject(()=>SpreadsheetService.Import(rows,"rollback.csv","rollback-hash",true,true),"Import failure is reported");conn.Execute("DROP TRIGGER fail_import");
            Check(repository.Count(null,null,null)==25 && conn.ExecuteScalar<int>("SELECT COUNT(*) FROM item_types WHERE name='Rollback item'")==0,"Failed import rolls back assets and new references");
            var export=Path.Combine(root,"inventory.xlsx");SpreadsheetService.Export(export);
            using(var workbook=new XLWorkbook(export)){Check(workbook.Worksheet(1).LastRowUsed()!.RowNumber()==26,"Excel export includes all assets and headers");}
            var exported=SpreadsheetService.Read(export,"Inventaris");Check(SpreadsheetService.Guess("Building",exported.Headers)==3,"Export headers map correctly on import");
            conn.Execute("UPDATE assets SET notes='=1+1' WHERE id=@id",new{id=asset.Id});var exportCsv=Path.Combine(root,"inventory.csv");SpreadsheetService.Export(exportCsv);Check(File.ReadAllText(exportCsv).Contains("'=1+1"),"CSV formula-like values neutralized");
            var backup=Path.Combine(root,"snapshot.hana");BackupService.Create(backup,Password);
            using(var copy=DatabaseVault.Open(backup,Password,true)){Check(copy.ExecuteScalar<int>("SELECT COUNT(*) FROM assets")==25,"Encrypted backup includes committed WAL data");Check(copy.ExecuteScalar<string>("PRAGMA integrity_check")=="ok","Encrypted backup verified");}
            Reject(()=>{using var copy=DatabaseVault.Open(backup,"wrong",true);copy.ExecuteScalar<int>("SELECT COUNT(*) FROM assets");},"Backup cannot be read with wrong password");
            Reject(()=>{using var copy=DatabaseVault.Open(AppConfig.DatabasePath,"",true);copy.ExecuteScalar<int>("SELECT COUNT(*) FROM assets");},"Live database cannot be read without key");
            Check(!File.ReadAllText(AppConfig.DatabasePath+".key").Contains(new SqliteConnectionStringBuilder(AppConfig.ConnectionString).Password),"Key sidecar does not contain plaintext key");
            Reject(()=>BackupService.RestoreFresh(backup,Password),"Restore cannot overwrite populated installation");
            Child("--restore",Path.Combine(root,"restore","inventory.db"),backup,"25");
            // Create an isolated legacy plaintext fixture, then verify real startup conversion.
            var legacy=Path.Combine(root,"legacy.db");DatabaseVault.Export(conn,legacy,"");
            using(var old=DatabaseVault.Open(legacy,"")){old.Execute("DROP VIEW v_asset_list; DROP TRIGGER asset_building_insert; DROP TRIGGER asset_building_update; ALTER TABLE assets DROP COLUMN building_id; DROP TABLE user_access; PRAGMA user_version=1;");}
            Child("--upgrade",legacy,"25");
            V04LocalChecks.Run(admin,item,building,root);
            V04CloudChecks.Run().GetAwaiter().GetResult();
            Child("--cloud-only",Path.Combine(root,"cloud-only-source","inventory.db"));
            if(Environment.GetEnvironmentVariable("HANA_LIVE_TESTS")=="1")CloudLiveChecks.Run().GetAwaiter().GetResult();
            var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var main=new MainWindow();var vm=(InventoryApp.ViewModels.MainViewModel)main.DataContext;vm.SelectedAsset=vm.Assets.First();
            var cloudInventory=PreviewCloudInventory();
            var windows=new (string,Window)[]{("main",main),("add",new AddAssetWindow(vm)),("references",new MasterDataWindow()),("accounts",new AccountWindow()),("import",new ImportWindow(csv)),("guide",new GuideWindow()),("setup",new SetupWindow()),("cloud-login",new CloudWindow()),("duplicate",new DuplicateWindow(new[]{"TEST-0001"})),("cloud-inventory",cloudInventory)};
            Check(GuideWindow.ShouldShow(),"Initial guide shown until user dismisses preference");
            if(args.Length>0){Directory.CreateDirectory(args[0]);foreach(var (name,window) in windows)Render(window,Path.Combine(args[0],name+".png"));}
            Check(windows.Length==10,"Ten UI screens constructed (rendered when preview directory supplied)");
            Session.CurrentUser=auth.Login("viewercheck","New-test-password-2026");var viewerMain=new MainWindow();
            Check(!((InventoryApp.ViewModels.MainViewModel)viewerMain.DataContext).IsAdmin,"Viewer screen has read-only controls");
            if(args.Length>0)Render(viewerMain,Path.Combine(args[0],"viewer.png"));
            Console.WriteLine($"SUCCESS: {_checks} parent checks plus child recovery/migration checks. Fixtures: {root}");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static CloudWindow PreviewCloudInventory()
    {
        var window=new CloudWindow();
        var api=(CloudClient)typeof(CloudWindow).GetField("_api",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        typeof(CloudClient).GetProperty("Session")!.SetValue(api,new CloudSession{Profile=new(){Id=Guid.NewGuid(),Email="preview@example.invalid",Role="ADMIN",Active=true},ExpiresAt=long.MaxValue});
        typeof(CloudWindow).GetMethod("OpenInventory",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);
        return window;
    }
    private static void Render(Window window,string path)
    {
        var content=(FrameworkElement)window.Content;content.Measure(new Size(window.Width,window.Height));content.Arrange(new Rect(0,0,window.Width,window.Height));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(243,245,247)),null,new Rect(0,0,window.Width,window.Height));bitmap.Render(background);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}

