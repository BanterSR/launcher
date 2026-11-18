using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
//
// Installing a release: the release asset MomokoLauncher.exe is downloaded
// next to the running executable as <exe>.update and must match the size and
// SHA-256 digest GitHub reports for it. The running executable is renamed to
// <exe>.old (Windows allows renaming a running image), the download takes its
// place, and the new process is started with --after-update <pid>: it waits
// for this process to exit before taking the single-instance mutex, then
// deletes <exe>.old.
internal sealed class UpdateClient:IDisposable
{
    public const string ReleaseAsset="MomokoLauncher.exe";
    public const string AfterUpdateArgument="--after-update";
    private readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(15)};
    private readonly HttpClient download=new(){Timeout=TimeSpan.FromMinutes(10)};

    public UpdateClient()
    {
        var agent=new ProductInfoHeaderValue("MomokoLauncher",Current?.ToString()??"local");
        http.DefaultRequestHeaders.UserAgent.Add(agent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
        download.DefaultRequestHeaders.UserAgent.Add(agent);
    }

    private static string Executable=>Environment.ProcessPath??throw new InvalidOperationException("无法确定启动器程序的位置。");
    private static string Staged=>Executable+".update";
    private static string Previous=>Executable+".old";

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
            // Only an asset with a SHA-256 digest can be installed; GitHub
            // reports "sha256:<hex>" for uploaded release assets.
            var digest=asset?.Digest is {} value&&value.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)&&value.Length==7+64
                ?value[7..].ToLowerInvariant():null;
            var installable=asset is not null&&digest is not null&&asset.Size>0;
            return new LauncherRelease(version,release.TagName,release.Name??release.TagName,release.HtmlUrl,
                installable?new LauncherAsset(asset!.BrowserDownloadUrl,asset.Size,digest!):null,
                release.Body??"",release.PublishedAt);
        }
    }

    // Downloads the release executable to <exe>.update and checks its size,
    // SHA-256 digest and PE signature. Progress reports whole percents.
    public async Task<string> DownloadAsync(LauncherAsset asset,IProgress<int> progress,CancellationToken cancellation)
    {
        var target=Staged;
        try
        {
            HttpResponseMessage response;
            try{response=await download.GetAsync(asset.Url,HttpCompletionOption.ResponseHeadersRead,cancellation);}
            catch(HttpRequestException error){throw new UpdateUnavailableException("无法下载新版本："+error.Message,error);}
            catch(TaskCanceledException error) when(!cancellation.IsCancellationRequested)
            {throw new UpdateUnavailableException("下载新版本超时。",error);}
            using(response)
            {
                if(!response.IsSuccessStatusCode)throw new UpdateUnavailableException($"下载新版本失败（HTTP {(int)response.StatusCode}）。");
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using(var source=await response.Content.ReadAsStreamAsync(cancellation))
                await using(var file=File.Create(target))
                {
                    var buffer=new byte[81920];
                    long total=0;
                    var reported=-1;
                    int read;
                    while((read=await source.ReadAsync(buffer,cancellation))>0)
                    {
                        total+=read;
                        if(total>asset.Size)throw new UpdateUnavailableException("下载的文件比发布信息里的大，已放弃更新。");
                        hash.AppendData(buffer,0,read);
                        await file.WriteAsync(buffer.AsMemory(0,read),cancellation);
                        var percent=(int)(total*100/asset.Size);
                        if(percent!=reported){reported=percent;progress.Report(percent);}
                    }
                    if(total!=asset.Size)throw new UpdateUnavailableException("下载的文件不完整，已放弃更新。");
                }
                if(Convert.ToHexStringLower(hash.GetHashAndReset())!=asset.Sha256)
                    throw new UpdateUnavailableException("下载的文件校验失败（SHA-256 与发布信息不一致），已放弃更新。");
            }
            using(var check=File.OpenRead(target))
            {
                if(check.ReadByte()!='M'||check.ReadByte()!='Z')throw new UpdateUnavailableException("下载的文件不是 Windows 程序，已放弃更新。");
            }
            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    // Puts the downloaded executable in place of the running one.
    public static void Install(string staged)
    {
        var executable=Executable;
        File.Move(executable,Previous,true);
        try{File.Move(staged,executable);}
        catch
        {
            File.Move(Previous,executable,true);
            throw;
        }
    }

    // Starts the installed executable; it waits for this process to exit.
    public static void Restart()
    {
        var executable=Executable;
        var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!};
        start.ArgumentList.Add(AfterUpdateArgument);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        using var process=Process.Start(start)??throw new InvalidOperationException("无法启动新版本的启动器。");
    }

    // Started by Restart: wait until the previous launcher process has exited.
    public static void WaitForPrevious(string[] arguments)
    {
        if(arguments.Length!=2||arguments[0]!=AfterUpdateArgument||
           !int.TryParse(arguments[1],NumberStyles.None,CultureInfo.InvariantCulture,out var id))return;
        try
        {
            using var previous=Process.GetProcessById(id);
            previous.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch(ArgumentException){}
    }

    // Leftovers of an update: the replaced executable and an unfinished download.
    public static void CleanupPrevious()
    {
        TryDelete(Previous);
        TryDelete(Staged);
    }

    private static void TryDelete(string path)
    {
        try{if(File.Exists(path))File.Delete(path);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {LauncherLog.Warn($"无法删除更新残留 {path}：{error.Message}");}
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

    public void Dispose()
    {
        http.Dispose();
        download.Dispose();
    }
}

// Asset: the installable MomokoLauncher.exe of the release, null when the
// release has none (or GitHub reports no SHA-256 digest for it).
internal sealed record LauncherRelease(Version Version,string Tag,string Name,string Page,LauncherAsset? Asset,string Notes,DateTimeOffset? PublishedAt);

internal sealed record LauncherAsset(string Url,long Size,string Sha256);

internal sealed class UpdateUnavailableException(string message,Exception? inner=null):Exception(message,inner);

internal sealed record GitHubRelease(string TagName,string? Name,string HtmlUrl,string? Body,DateTimeOffset? PublishedAt,GitHubAsset[] Assets);
internal sealed record GitHubAsset(string Name,string BrowserDownloadUrl,long Size,string? Digest);

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubJson:JsonSerializerContext;
