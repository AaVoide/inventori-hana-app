using System.Text.Json;
using InventoryApp.Services;

internal static class CloudLiveChecks
{
    public static async Task Run()
    {
        var password=Environment.GetEnvironmentVariable("HANA_TEST_PASSWORD")??throw new InvalidOperationException("Test password environment missing");
        var editorEmail=Environment.GetEnvironmentVariable("HANA_TEST_EDITOR")!;
        var viewerEmail=Environment.GetEnvironmentVariable("HANA_TEST_VIEWER")!;
        var first=new CloudClient();await first.LoginAsync(editorEmail,password);
        Program.Check(first.Session!.Profile.Role=="EDITOR","Live Supabase login obtains server Editor role");
        var id=Guid.NewGuid();var operation=Guid.NewGuid();var data=new CloudData{Code="REVIEW-TEST-"+id.ToString("N"),Name="Review test asset",Building="Review test building"};
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
        await viewer.LogoutAsync();await second.LogoutAsync();await first.LogoutAsync();
    }
}
