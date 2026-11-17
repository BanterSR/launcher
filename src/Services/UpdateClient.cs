using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Momoko.Launcher.Core;

namespace Momoko.Launcher.Services;

// Update check: the latest published release of the launcher repository on
// GitHub (GET https://api.github.com/repos/{owner}/{repo}/releases/latest;
// drafts and pre-releases are never "latest"). Release tags are vX.Y.Z, the
// release workflow builds with the same version. Local builds (no X.Y.Z
// version compiled in) never check. Requests go through the system proxy,
// since GitHub is often reachable only through one.
internal sealed class UpdateClient:IDisposable
{
    public const string ReleaseAsset="MomokoLauncher.exe";
    private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(15)};

    public UpdateClient()
    {
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MomokoLauncher",Current?.ToString()??"local"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
    }

    // The compiled-in release version, or null for a local build.
    public static Version? Current=>ParseVersion(BuildInfo.Version);
    public static string Repository=>BuildInfo.UpdateRepository;
    public static string ReleasesPage=>$"https://github.com/{Repository}/releases";

    // The latest release, whether or not it is newer than this build.
    public async Task<LauncherRelease> LatestAsync(CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try{response=await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest",cancellation);}
        catch(HttpRequestException error){throw new UpdateUnavailableException("无法连接 GitHub："+error.Message,error);}
        catch(TaskCanceledException error) when(!cancellation.IsCancellationRequested)
        {throw new UpdateUnavailableException("连接 GitHub 超时。",error);}
        using(response)
        {
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound)throw new UpdateUnavailableException("仓库 "+Repository+" 还没有发布版本。");
            if(!response.IsSuccessStatusCode)throw new UpdateUnavailableException($"GitHub 返回错误（HTTP {(int)response.StatusCode}）。");
            GitHubRelease? release;
            try{release=JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(cancellation),GitHubJson.Default.GitHubRelease);}
            catch(JsonException error){throw new UpdateUnavailableException("GitHub 返回了无法识别的发布信息。",error);}
            if(release is null)throw new UpdateUnavailableException("GitHub 返回了空的发布信息。");
            var version=ParseVersion(release.TagName.StartsWith('v')?release.TagName[1..]:release.TagName)
                ??throw new UpdateUnavailableException("最新发布的标签不是 vX.Y.Z："+release.TagName);
            var asset=release.Assets.FirstOrDefault(item=>string.Equals(item.Name,ReleaseAsset,StringComparison.OrdinalIgnoreCase));
            return new LauncherRelease(version,release.TagName,release.Name??release.TagName,release.HtmlUrl,asset?.BrowserDownloadUrl,
                release.Body??"",release.PublishedAt);
        }
    }

    // X.Y.Z only; anything else (such as 0.0.0-local) is not a release version.
    private static Version? ParseVersion(string text)
    {
        var parts=text.Split('.');
        if(parts.Length!=3)return null;
        var numbers=new int[3];
        for(var i=0;i<3;i++)
            if(!int.TryParse(parts[i],NumberStyles.None,CultureInfo.InvariantCulture,out numbers[i]))return null;
        return new Version(numbers[0],numbers[1],numbers[2]);
    }

    public void Dispose()=>http.Dispose();
}

internal sealed record LauncherRelease(Version Version,string Tag,string Name,string Page,string? Download,string Notes,DateTimeOffset? PublishedAt);

internal sealed class UpdateUnavailableException(string message,Exception? inner=null):Exception(message,inner);

internal sealed record GitHubRelease(string TagName,string? Name,string HtmlUrl,string? Body,DateTimeOffset? PublishedAt,GitHubAsset[] Assets);
internal sealed record GitHubAsset(string Name,string BrowserDownloadUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubJson:JsonSerializerContext;
