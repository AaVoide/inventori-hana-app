using System.Text.Json;
using System.Net.Http;
using InventoryApp.Services;

internal static class CloudLiveChecks
{
    private sealed class InterruptedApi:ICloudApi
    {
        private readonly CloudClient _inner;
        public bool Offline,DropNextAcknowledgement;
        public InterruptedApi(CloudClient inner){_inner=inner;}
        public CloudSession? Session=>_inner.Session;
        public Task<CloudProfile> ProfileAsync()=>Offline?throw new HttpRequestException("Simulated disconnected device"): _inner.ProfileAsync();
        public async Task<string> RpcAsync(string name,object body)
        {
            var response=await _inner.RpcAsync(name,body);
            if(DropNextAcknowledgement){DropNextAcknowledgement=false;throw new HttpRequestException("Simulated lost acknowledgement after real server commit");}
            return response;
        }
        public Task<List<CloudAsset>> PullAsync(long revision)=>_inner.PullAsync(revision);
    }
    public static async Task Run()
    {
        if(!Guid.TryParse(Environment.GetEnvironmentVariable("HANA_MAINTENANCE_RUN"),out var runId))
            throw new InvalidOperationException("Append a STARTED maintenance entry first and supply HANA_MAINTENANCE_RUN. See AGENTS.md.");
        Console.WriteLine("Maintenance run: "+runId);
        var password=Environment.GetEnvironmentVariable("HANA_TEST_PASSWORD")??throw new InvalidOperationException("Test password environment missing");
        var editorEmail=Environment.GetEnvironmentVariable("HANA_TEST_EDITOR")!;
        var viewerEmail=Environment.GetEnvironmentVariable("HANA_TEST_VIEWER")!;
        var first=new CloudClient();await first.LoginAsync(editorEmail,password);
        Program.Check(first.Session!.Profile.Role=="EDITOR","Live Supabase login obtains server Editor role");
        var id=Guid.NewGuid();var operation=Guid.NewGuid();var data=new CloudData{Code="REVIEW-TEST-"+id.ToString("N"),Name="Review test asset "+id.ToString("N")[..8],Building="Review test building"};
        var request=new{p_operation=operation,p_id=id,p_expected=0,p_data=data,p_override=false};
        var result=await first.RpcAsync("hana_apply_asset",request);
        using(var doc=JsonDocument.Parse(result))Program.Check(doc.RootElement.GetProperty("status").GetString()=="APPLIED","Live asset save persists centrally");
        var repeat=await first.RpcAsync("hana_apply_asset",request);
        Program.Check(result==repeat,"Live retry returns identical operation receipt");
        var second=new CloudClient();await second.LoginAsync(editorEmail,password);
        var rows=await second.PullAsync(0);
        Program.Check(rows.Any(a=>a.Id==id&&a.Code==data.Code),"Second authenticated client reads persisted asset");
        data.Notes="device two edit";
        await second.RpcAsync("hana_apply_asset",new{p_operation=Guid.NewGuid(),p_id=id,p_expected=1,p_data=data,p_override=false});
        data.Notes="stale device one edit";
        var conflict=await first.RpcAsync("hana_apply_asset",new{p_operation=Guid.NewGuid(),p_id=id,p_expected=1,p_data=data,p_override=false});
        using(var doc=JsonDocument.Parse(conflict))Program.Check(doc.RootElement.GetProperty("status").GetString()=="CONFLICT","Live two-client stale edit returns conflict");
        var viewer=new CloudClient();await viewer.LoginAsync(viewerEmail,password);
        Program.Check(viewer.Session!.Profile.Role=="VIEWER","Live Viewer login uses server role");
        try{await viewer.RpcAsync("hana_apply_asset",new{p_operation=Guid.NewGuid(),p_id=Guid.NewGuid(),p_expected=0,p_data=data,p_override=true});throw new Exception("Viewer write succeeded");}
        catch(CloudFailure ex) when(ex.Status==System.Net.HttpStatusCode.Forbidden){Program.Check(true,"Live Viewer write blocked by server");}
        first.Session.ExpiresAt=0;
        Program.Check((await first.ProfileAsync()).Role=="EDITOR","Expired access token refreshes against live Auth");
        var queue=new CloudStore();var queuedId=Guid.NewGuid();
        queue.Queue(first.Session.Profile.Id,new(){Id=queuedId,Data=new(){Code="REVIEW-TEST-"+queuedId.ToString("N"),Name="Offline review "+queuedId.ToString("N")[..8],Building="Review test building"}},false);
        var interrupted=new InterruptedApi(first){Offline=true};var sync=new CloudSync(interrupted,queue);
        await sync.RunAsync();
        Program.Check(queue.Pending().Any(p=>p.Asset==queuedId.ToString()),"Live-client disconnected mode retains encrypted queue");
        interrupted.Offline=false;interrupted.DropNextAcknowledgement=true;await sync.RunAsync();
        Program.Check(new CloudStore().Pending().Any(p=>p.Asset==queuedId.ToString()),"Queue survives reopening after server commit with lost acknowledgement");
        await sync.RunAsync();
        Program.Check(queue.Pending().All(p=>p.Asset!=queuedId.ToString()),"Reconnection confirms live server receipt and clears queue");
        Program.Check((await second.PullAsync(0)).Count(a=>a.Id==queuedId)==1,"Second client sees exactly one asset after queued retry");
        using(var audit=JsonDocument.Parse(await first.AuditAsync(queuedId)))
            Program.Check(audit.RootElement.GetArrayLength()==1,"Live queued retry creates exactly one audit event");
        await viewer.LogoutAsync();await second.LogoutAsync();await first.LogoutAsync();
    }
}
