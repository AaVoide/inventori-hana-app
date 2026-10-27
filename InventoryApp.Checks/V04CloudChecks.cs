using System.Net.Http;
using System.IO;
using System.Text.Json;
using InventoryApp.Services;

internal static class V04CloudChecks
{
    private sealed class FakeApi:ICloudApi
    {
        public CloudSession? Session {get;}=new(){Profile=new(){Id=Guid.NewGuid(),Role="EDITOR",Active=true}};
        public bool DropAcknowledgement,Offline,Conflict;
        public int Applied;
        private readonly Dictionary<string,string> _receipts=new();
        private readonly List<CloudAsset> _rows=new();
        public Task<CloudProfile> ProfileAsync()=>Offline?throw new HttpRequestException("simulated offline"):Task.FromResult(Session!.Profile);
        public Task<string> RpcAsync(string name,object body)
        {
            using var doc=JsonDocument.Parse(CloudJson.Write(body));var r=doc.RootElement;var op=r.GetProperty("p_operation").GetString()!;
            if(_receipts.TryGetValue(op,out var previous))return Task.FromResult(previous);
            var a=new CloudAsset{Id=r.GetProperty("p_id").GetGuid(),Data=CloudJson.Read<CloudData>(r.GetProperty("p_data").GetRawText()),Version=1,Revision=Applied+1};a.Code=a.Data.Code;
            var response=CloudJson.Write(new{status=Conflict?"CONFLICT":"APPLIED",server=a});
            _receipts.Add(op,response);
            if(!Conflict){_rows.Add(a);Applied++;}
            if(DropAcknowledgement){DropAcknowledgement=false;throw new HttpRequestException("applied but connection lost");}
            return Task.FromResult(response);
        }
        public Task<List<CloudAsset>> PullAsync(long revision)=>Task.FromResult(_rows.Where(a=>a.Revision>revision).ToList());
    }
    public static async Task Run()
    {
        var store=new CloudStore();var api=new FakeApi();var sync=new CloudSync(api,store);
        var asset=new CloudAsset{Id=Guid.NewGuid(),Code="CLOUD-CHECK",Data=new(){Code="CLOUD-CHECK",Name="Test chair",Building="Test building"}};
        store.Queue(api.Session!.Profile.Id,asset,false);
        Program.Check(new CloudStore().Pending().Count==1,"Cloud outbox survives repository reopen");
        api.Offline=true;await sync.RunAsync();
        Program.Check(store.Pending().Count==1&&api.Applied==0,"Offline sync retains queued operation");
        api.Offline=false;api.DropAcknowledgement=true;await sync.RunAsync();
        Program.Check(store.Pending().Count==1&&api.Applied==1,"Lost acknowledgement retains immutable retry operation");
        await sync.RunAsync();
        Program.Check(store.Pending().Count==0&&api.Applied==1,"Retry confirms receipt without repeated upload");
        Program.Check(store.Assets().Single(a=>a.Id==asset.Id).Version==1,"Server version persisted in encrypted cache");
        var stale=store.Assets().Single(a=>a.Id==asset.Id);stale.Data.Notes="local edit";
        store.Queue(api.Session.Profile.Id,stale,false);api.Conflict=true;await sync.RunAsync();
        Program.Check(store.Pending().Single().State=="CONFLICT","Server editing conflict retained for review");
        Program.Check(store.Assets().Single(a=>a.Id==asset.Id).Data.Notes=="local edit","Pull never hides pending local changes");
        Program.Reject(()=>store.Resolve(store.Pending().Single(),Guid.NewGuid(),false),"Another account cannot discard queued edits");
        store.Resolve(store.Pending().Single(),api.Session.Profile.Id,false);
        Program.Check(store.Pending().Count==0,"Explicit server choice resolves conflict and retains recovery record");
        Program.Check(!CloudJson.Write(new CloudData()).Contains("legacy_history"),"Ordinary Editor payload omits privileged legacy transfer field");
        // Backup round trip includes the cache/outbox tables alongside the old inventory.
        store.Queue(api.Session.Profile.Id,new(){Id=Guid.NewGuid(),Data=new(){Code="RECOVERY-CLOUD",Name="Recovery",Building="Building"}},false);
        using var c=InventoryApp.Data.Db.Open();var path=Path.Combine(Path.GetDirectoryName(InventoryApp.AppConfig.DatabasePath)!,"cloud-roundtrip.hanaz");
        BackupBundle.Create(c,path,"Test-only-cloud-recovery");var restored=path+".db";BackupBundle.RestoreTo(path,"Test-only-cloud-recovery",restored,"test-cloud-key");
        using(var copy=DatabaseVault.Open(restored,"test-cloud-key",true))
            Program.Check(Dapper.SqlMapper.ExecuteScalar<int>(copy,"SELECT COUNT(*) FROM cloud_queue")==1,"Encrypted restore preserves offline queue");
    }
}
