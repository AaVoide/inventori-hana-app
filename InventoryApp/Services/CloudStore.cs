using System.Text.Json;
using System.Net.Http;
using Dapper;
using InventoryApp.Data;

namespace InventoryApp.Services;

public sealed class CloudPending
{
    public string Operation {get;set;}="";
    public string Actor {get;set;}="";
    public string Asset {get;set;}="";
    public long Expected {get;set;}
    public string Payload {get;set;}="";
    public bool Override {get;set;}
    public string State {get;set;}="PENDING";
    public string? Response {get;set;}
}
public sealed class CloudStore
{
    public CloudStore()
    {
        using var c=Db.Open();
        if(c.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name='cloud_cache'")==0&&c.ExecuteScalar<int>("SELECT COUNT(*) FROM users")>0)
            AutomaticBackup.SnapshotBeforeMigration(c);
        using var tx=c.BeginTransaction();
        c.Execute(@"CREATE TABLE IF NOT EXISTS cloud_cache(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS cloud_queue(operation TEXT PRIMARY KEY,actor TEXT NOT NULL,asset TEXT NOT NULL UNIQUE,expected INTEGER NOT NULL,payload TEXT NOT NULL,override INTEGER NOT NULL,state TEXT NOT NULL,response TEXT);
            CREATE TABLE IF NOT EXISTS cloud_state(key TEXT PRIMARY KEY,value TEXT NOT NULL);",transaction:tx);
        tx.Commit();
    }
    public List<CloudPending> Pending()=>ReadQueue();
    private List<CloudPending> ReadQueue(){using var c=Db.Open();return c.Query<CloudPending>("SELECT * FROM cloud_queue ORDER BY rowid").AsList();}
    public List<CloudAsset> Assets()
    {
        using var c=Db.Open();var assets=c.Query<string>("SELECT payload FROM cloud_cache").Select(CloudJson.Read<CloudAsset>).ToDictionary(a=>a.Id);
        foreach(var p in ReadQueue())assets[Guid.Parse(p.Asset)]=new(){Id=Guid.Parse(p.Asset),Data=CloudJson.Read<CloudData>(p.Payload),Code=CloudJson.Read<CloudData>(p.Payload).Code,Version=p.Expected,State=p.State};
        return assets.Values.OrderBy(a=>a.Data.Name).ThenBy(a=>a.Code).ToList();
    }
    public void Queue(Guid actor,CloudAsset asset,bool duplicateOverride)
    {
        if(string.IsNullOrWhiteSpace(asset.Data.Name)||string.IsNullOrWhiteSpace(asset.Data.Building))throw new ArgumentException("Nama barang dan gedung wajib.");
        if(asset.Data.Name.Trim().Length>150||asset.Data.Building.Trim().Length>150)throw new ArgumentException("Nama barang dan gedung maksimal 150 karakter.");
        if(asset.Data.Code.Length is <1 or >100)throw new ArgumentException("Kode permanen harus berisi 1–100 karakter.");
        if(System.Text.Encoding.UTF8.GetByteCount(CloudJson.Write(asset.Data))>1_000_000)throw new ArgumentException("Data/riwayat aset terlalu besar untuk dikirim pada build tinjauan ini.");
        if(asset.Data.AcquisitionYear is <1900 or >2100)throw new ArgumentException("Tahun antara 1900–2100.");
        if(asset.Data.Status is not ("ACTIVE" or "INACTIVE" or "DISPOSED"))throw new ArgumentException("Status tidak valid.");
        using var c=Db.Open();using var tx=c.BeginTransaction();
        if(c.ExecuteScalar<int>("SELECT COUNT(*) FROM cloud_queue WHERE asset=@id",new{id=asset.Id.ToString()},tx)>0)
            throw new InvalidOperationException("Aset masih antre atau berkonflik. Sinkronkan atau selesaikan dahulu.");
        c.Execute("INSERT INTO cloud_queue(operation,actor,asset,expected,payload,override,state) VALUES(@op,@actor,@asset,@expected,@payload,@override,'PENDING')",
            new{op=Guid.NewGuid().ToString(),actor=actor.ToString(),asset=asset.Id.ToString(),expected=asset.Version,payload=CloudJson.Write(asset.Data),@override=duplicateOverride},tx);
        Dirty(c,tx);tx.Commit();
    }
    private static void Dirty(Microsoft.Data.Sqlite.SqliteConnection c,Microsoft.Data.Sqlite.SqliteTransaction tx)=>c.Execute(@"INSERT INTO app_settings(setting_key,setting_value) VALUES('cloud_change_revision','1')
        ON CONFLICT(setting_key) DO UPDATE SET setting_value=CAST(setting_value AS INTEGER)+1",transaction:tx);
    public long Cursor {get{using var c=Db.Open();return c.ExecuteScalar<long?>("SELECT CAST(value AS INTEGER) FROM cloud_state WHERE key='cursor'")??0;}}
    public void Receive(List<CloudAsset> rows)
    {
        if(rows.Count==0)return;
        using var c=Db.Open();using var tx=c.BeginTransaction();
        foreach(var row in rows)c.Execute("INSERT INTO cloud_cache VALUES(@id,@payload) ON CONFLICT(id) DO UPDATE SET payload=@payload",new{id=row.Id.ToString(),payload=CloudJson.Write(row)},tx);
        c.Execute("INSERT INTO cloud_state VALUES('cursor',@value) ON CONFLICT(key) DO UPDATE SET value=@value",new{value=rows.Max(r=>r.Revision).ToString()},tx);
        Dirty(c,tx);tx.Commit();
    }
    public void Complete(CloudPending op,string response)
    {
        using var result=JsonDocument.Parse(response);var status=result.RootElement.GetProperty("status").GetString()!;
        using var c=Db.Open();using var tx=c.BeginTransaction();
        if(status=="APPLIED")
        {
            var server=result.RootElement.GetProperty("server").GetRawText();
            c.Execute("INSERT INTO cloud_cache VALUES(@id,@payload) ON CONFLICT(id) DO UPDATE SET payload=@payload",new{id=op.Asset,payload=server},tx);
            c.Execute("DELETE FROM cloud_queue WHERE operation=@Operation",op,tx);
        }
        else c.Execute("UPDATE cloud_queue SET state=@status,response=@response WHERE operation=@Operation",new{status,response,op.Operation},tx);
        Dirty(c,tx);tx.Commit();
    }
    public void Resolve(CloudPending op,Guid actor,bool keepLocal,bool duplicateOverride=false)
    {
        if(op.Actor!=actor.ToString())throw new UnauthorizedAccessException("Hanya pembuat perubahan yang dapat menyelesaikan antrean ini.");
        if(op.State=="PENDING")throw new InvalidOperationException("Operasi belum memperoleh jawaban server. Coba sinkron ulang dahulu.");
        using var c=Db.Open();using var tx=c.BeginTransaction();
        if(keepLocal)
        {
            if(op.State=="CODE_EXISTS")throw new InvalidOperationException("Kode permanen sudah ada. Periksa asal data; jangan menimpa aset lain.");
            using var response=JsonDocument.Parse(op.Response!);
            long expected=0;
            if(response.RootElement.TryGetProperty("server",out var server)&&server.ValueKind==JsonValueKind.Object)expected=server.GetProperty("version").GetInt64();
            c.Execute("UPDATE cloud_queue SET operation=@next,expected=@expected,state='PENDING',response=NULL,override=@duplicateOverride WHERE operation=@Operation",new{next=Guid.NewGuid().ToString(),expected,duplicateOverride,op.Operation},tx);
        }
        else
        {
            // Keep a durable record of discarded local work for recovery/audit.
            c.Execute("INSERT OR REPLACE INTO cloud_state VALUES(@key,@value)",new{key="resolved-"+op.Operation,value=CloudJson.Write(op)},tx);
            c.Execute("DELETE FROM cloud_queue WHERE operation=@Operation",op,tx);
        }
        Dirty(c,tx);tx.Commit();
    }
}
public sealed class CloudSync
{
    private readonly ICloudApi _api;private readonly CloudStore _store;
    private readonly SemaphoreSlim _gate=new(1,1);
    public CloudSync(ICloudApi api,CloudStore store){_api=api;_store=store;}
    public async Task<string> RunAsync()
    {
        if(!await _gate.WaitAsync(0))return "Sinkronisasi sedang berjalan.";
        try
        {
            var profile=await _api.ProfileAsync();
            if(profile.Role is "ADMIN" or "EDITOR")
            foreach(var pending in _store.Pending().Where(p=>p.Actor==profile.Id.ToString()&&p.State=="PENDING"))
            {
                var result=await _api.RpcAsync("hana_apply_asset",new{p_operation=pending.Operation,p_id=pending.Asset,p_expected=pending.Expected,
                    p_data=CloudJson.Read<CloudData>(pending.Payload),p_override=pending.Override});
                _store.Complete(pending,result);
            }
            while(true){var rows=await _api.PullAsync(_store.Cursor);_store.Receive(rows);if(rows.Count<500)break;}
            var queue=_store.Pending();
            return $"Terhubung · {queue.Count(p=>p.State=="PENDING")} antre · {queue.Count(p=>p.State!="PENDING")} perlu diperiksa";
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException){return "Offline / layanan belum tersedia. Antrean tersimpan. Hubungi Admin bila berlanjut.";}
        finally{_gate.Release();}
    }
}
