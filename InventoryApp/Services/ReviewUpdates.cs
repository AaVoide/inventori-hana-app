using System.Diagnostics;
using System.Net.Http;
using System.Reflection;

namespace InventoryApp.Services;

public static class ReviewUpdates
{
    public const string ReleasesUrl="https://github.com/AaVoide/inventori-hana-app/releases";
    public static async Task<string> CheckAsync()
    {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(8)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Inventori-HANA/0.4-review");
        using var response=await http.GetAsync("https://api.github.com/repos/AaVoide/inventori-hana-app/releases?per_page=1");
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)
            return "Pembaruan: repositori privat. Buka Rilis GitHub dengan akun yang memiliki akses.";
        if(!response.IsSuccessStatusCode)return "Pembaruan belum dapat diperiksa.";
        using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if(json.RootElement.GetArrayLength()==0)return "Belum ada rilis GitHub.";
        var latest=json.RootElement[0].GetProperty("tag_name").GetString();
        var current=typeof(ReviewUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]??"0.4-review";
        return "Rilis GitHub terbaru: "+latest+". Build ini: v"+current+". Buka Rilis GitHub untuk memeriksa.";
    }
    public static void Open()=>Process.Start(new ProcessStartInfo(ReleasesUrl){UseShellExecute=true});
}
