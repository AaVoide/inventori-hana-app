using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InventoryApp.Services;

public static class CloudJson
{
    public static readonly JsonSerializerOptions Options=new(){PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower,PropertyNameCaseInsensitive=true,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    public static string Write<T>(T value)=>JsonSerializer.Serialize(value,Options);
    public static T Read<T>(string value)=>JsonSerializer.Deserialize<T>(value,Options)??throw new InvalidDataException("Respons kosong.");
}
public sealed class CloudData
{
    public string Code {get;set;}="";
    public string Name {get;set;}="";
    public string Category {get;set;}="";
    public string Building {get;set;}="";
    public string Room {get;set;}="";
    public string Condition {get;set;}="Baik";
    public string Status {get;set;}="ACTIVE";
    public int? AcquisitionYear {get;set;}
    public string Notes {get;set;}="";
    public string? LegacyCode {get;set;}
    public JsonElement? LegacyHistory {get;set;}
}
public sealed class CloudAsset
{
    public Guid Id {get;set;}
    public string Code {get;set;}="";
    public CloudData Data {get;set;}=new();
    public long Version {get;set;}
    public long Revision {get;set;}
    public string Name=>Data.Name;
    public string Location=>Data.Building+(Data.Room.Length>0?" / "+Data.Room:"");
    public string State {get;set;}="Tersinkron";
}
public sealed class CloudProfile
{
    public Guid Id {get;set;}
    public string Email {get;set;}="";
    public string Role {get;set;}="VIEWER";
    public bool Active {get;set;}
}
public sealed class CloudSession
{
    public string AccessToken {get;set;}="";
    public string RefreshToken {get;set;}="";
    public long ExpiresAt {get;set;}
    public CloudProfile Profile {get;set;}=new();
}
public class CloudFailure : Exception
{
    public HttpStatusCode Status {get;}
    public CloudFailure(HttpStatusCode status,string message):base(message){Status=status;}
}
public interface ICloudApi
{
    CloudSession? Session {get;}
    Task<CloudProfile> ProfileAsync();
    Task<string> RpcAsync(string name,object body);
    Task<List<CloudAsset>> PullAsync(long revision);
}
public sealed class CloudClient : ICloudApi
{
    public const string ProjectUrl="https://vkzyeimylxnyrwhebqef.supabase.co";
    private const string PublishableKey="sb_publishable_7X3qY4QFkNwxsk7tM0sliA_SfhQb9BT";
    private readonly HttpClient _http=new(){Timeout=TimeSpan.FromSeconds(20)};
    private readonly SemaphoreSlim _refresh=new(1,1);
    public CloudSession? Session {get;private set;}
    private static string TokenFile=>Path.Combine(Path.GetDirectoryName(AppConfig.DatabasePath)!,"cloud-session.protected");
    public bool CanEdit=>Session?.Profile is {Active:true,Role:"ADMIN" or "EDITOR"};
    public CloudClient()
    {
        if(File.Exists(TokenFile))
        {
            try { Session=CloudJson.Read<CloudSession>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenFile),null,DataProtectionScope.CurrentUser))); }
            catch(Exception ex) when(ex is CryptographicException or JsonException or InvalidDataException){Session=null;}
        }
    }
    private void Persist()
    {
        if(Session is null){if(File.Exists(TokenFile))File.Delete(TokenFile);return;}
        var path=TokenFile+".writing";
        File.WriteAllBytes(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(CloudJson.Write(Session)),null,DataProtectionScope.CurrentUser));
        File.Move(path,TokenFile,true);
    }
    public async Task LoginAsync(string email,string password)
    {
        var response=await RequestAsync(HttpMethod.Post,"auth/v1/token?grant_type=password",new{email,password},false);
        AcceptToken(response);Session!.Profile=await ProfileAsync();Persist();
    }
    public async Task RegisterAsync(string email,string password)
    {
        if(password.Length<12)throw new ArgumentException("Kata sandi minimal 12 karakter.");
        await RequestAsync(HttpMethod.Post,"auth/v1/signup",new{email,password},false);
    }
    private void AcceptToken(string response)
    {
        using var doc=JsonDocument.Parse(response);var root=doc.RootElement;
        var previous=Session?.Profile;
        Session=new CloudSession{AccessToken=root.GetProperty("access_token").GetString()!,RefreshToken=root.GetProperty("refresh_token").GetString()!,
            ExpiresAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+root.GetProperty("expires_in").GetInt64(),
            Profile=previous??new CloudProfile{Id=root.GetProperty("user").GetProperty("id").GetGuid()}};
        // Never persist a newly selected account with the previous account's profile.
        Session.Profile=new CloudProfile{Id=root.GetProperty("user").GetProperty("id").GetGuid()};
    }
    private async Task RefreshAsync()
    {
        await _refresh.WaitAsync();
        try
        {
            if(Session is null)throw new UnauthorizedAccessException("Masuk ke cloud terlebih dahulu.");
            if(Session.ExpiresAt>DateTimeOffset.UtcNow.ToUnixTimeSeconds()+60)return;
            var oldProfile=Session.Profile;
            var token=await RequestAsync(HttpMethod.Post,"auth/v1/token?grant_type=refresh_token",new{refresh_token=Session.RefreshToken},false);
            AcceptToken(token);
            if(Session!.Profile.Id==oldProfile.Id)Session.Profile=oldProfile;
            Persist();
        }
        finally{_refresh.Release();}
    }
    private async Task<string> RequestAsync(HttpMethod method,string path,object? body,bool authenticated=true)
    {
        if(authenticated)await RefreshAsync();
        using var message=new HttpRequestMessage(method,ProjectUrl+"/"+path);
        message.Headers.Add("apikey",PublishableKey);
        if(authenticated)message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Session!.AccessToken);
        if(body is not null)message.Content=new StringContent(CloudJson.Write(body),Encoding.UTF8,"application/json");
        using var response=await _http.SendAsync(message);var text=await response.Content.ReadAsStringAsync();
        if(!response.IsSuccessStatusCode)
        {
            // Do not expose credentials, tokens, or entire server payloads in UI/logs.
            var explanation=response.StatusCode switch {
                HttpStatusCode.Unauthorized=>"Sesi berakhir. Masuk kembali ke cloud.",
                HttpStatusCode.Forbidden=>"Akses ditolak. Periksa peran akun atau hubungi Admin.",
                HttpStatusCode.BadRequest=>"Permintaan ditolak. Periksa email, kata sandi, atau data yang diisi.",
                (HttpStatusCode)429=>"Terlalu banyak permintaan. Coba beberapa saat lagi.",
                _=>"Layanan cloud belum tersedia. Data lokal tetap tersimpan; hubungi Admin."};
            throw new CloudFailure(response.StatusCode,explanation);
        }
        return text;
    }
    public async Task<CloudProfile> ProfileAsync()
    {
        var profiles=CloudJson.Read<List<CloudProfile>>(await RequestAsync(HttpMethod.Get,"rest/v1/hana_profiles?id=eq."+Session!.Profile.Id+"&select=*",null));
        var profile=profiles.SingleOrDefault()??throw new UnauthorizedAccessException("Profil cloud tidak tersedia.");
        Session.Profile=profile;Persist();
        if(!profile.Active)throw new UnauthorizedAccessException("Akun cloud dinonaktifkan. Hubungi Admin.");
        return profile;
    }
    public Task<string> RpcAsync(string name,object body)=>RequestAsync(HttpMethod.Post,"rest/v1/rpc/"+name,body);
    public async Task<List<CloudAsset>> PullAsync(long revision)=>CloudJson.Read<List<CloudAsset>>(await RequestAsync(HttpMethod.Get,
        "rest/v1/hana_assets?select=*&revision=gt."+revision+"&order=revision.asc&limit=500",null));
    public async Task<List<CloudProfile>> AccountsAsync()=>CloudJson.Read<List<CloudProfile>>(await RequestAsync(HttpMethod.Get,"rest/v1/hana_profiles?select=*&order=email",null));
    public async Task<string> AuditAsync(Guid id)=>await RequestAsync(HttpMethod.Get,"rest/v1/hana_audit?asset_id=eq."+id+"&order=id.desc&limit=100",null);
    public Task<string> MaintenanceLogAsync()=>RequestAsync(HttpMethod.Get,"rest/v1/hana_maintenance_log?select=*&order=id.desc&limit=100",null);
    public async Task LogoutAsync()
    {
        try{if(Session is not null)await RequestAsync(HttpMethod.Post,"auth/v1/logout?scope=local",null);}
        finally{Session=null;Persist();}
    }
}
