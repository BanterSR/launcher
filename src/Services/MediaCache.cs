using System.Security.Cryptography;
using System.Text;
using Momoko.Launcher.Core;

namespace Momoko.Launcher.Services;

// Game icons and backgrounds are links that Momoko hands out with the launch
// targets (files in its object storage). Each link is downloaded once into
// data\media and the page shows the local copy through the WebView2 host
// https://media.momoko.example/. A file is named after its link, so an
// unchanged link reuses the cached file; after every sync, files whose link
// is no longer handed out are deleted, so a changed link replaces the old file.
internal sealed class MediaCache:IDisposable
{
    public const string Host="media.momoko.example";
    private static readonly string[] VideoExtensions=[".mp4",".webm",".mov",".m4v",".mkv",".ogv",".avi"];
    private readonly HttpClient http=new(){Timeout=TimeSpan.FromMinutes(10)};
    private readonly SemaphoreSlim gate=new(1,1);

    public MediaCache(){Directory.CreateDirectory(AppPaths.Media);}

    // The page URL of a cached link, or null while it is not downloaded.
    public static string? Local(string? link)
    {
        var name=FileName(link);
        return name is not null&&File.Exists(Path.Combine(AppPaths.Media,name))?$"https://{Host}/{name}":null;
    }

    public static bool IsVideo(string? local)=>local is not null&&VideoExtensions.Contains(Path.GetExtension(local).ToLowerInvariant());

    // <sha256 of the link> plus the extension of the link path (kept so the
    // page can tell videos from images); null for links that are not http(s).
    private static string? FileName(string? link)
    {
        if(string.IsNullOrEmpty(link)||!Uri.TryCreate(link,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))return null;
        var extension=Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if(extension.Length>17||extension.Skip(1).Any(c=>!char.IsAsciiLetterOrDigit(c)))extension="";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(link)))+extension;
    }

    // Downloads the links that are not cached yet (reporting each finished
    // file), then deletes every cached file not named after one of the links.
    public async Task SyncAsync(IEnumerable<string?> links,Action changed,CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            var wanted=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var link in links)
            {
                var name=FileName(link);
                if(name is null){if(!string.IsNullOrEmpty(link))LauncherLog.Warn("启动器图片 / 视频链接不是 http(s)，已忽略："+link);continue;}
                wanted[name]=link!;
            }
            foreach(var (name,link) in wanted)
            {
                var target=Path.Combine(AppPaths.Media,name);
                if(File.Exists(target))continue;
                try
                {
                    await DownloadAsync(link,target,cancellation);
                    changed();
                }
                catch(Exception error) when(error is HttpRequestException or IOException
                    or TaskCanceledException&&!cancellation.IsCancellationRequested)
                {
                    LauncherLog.Warn($"下载启动器图片 / 视频失败：{link}：{error.Message}");
                }
            }
            foreach(var file in Directory.EnumerateFiles(AppPaths.Media))
            {
                if(wanted.ContainsKey(Path.GetFileName(file)))continue;
                try{File.Delete(file);}
                // A video still playing stays locked; the next sync deletes it.
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {
                    LauncherLog.Warn("旧的启动器缓存文件暂时无法删除，下次刷新时再删："+file);
                }
            }
        }
        finally{gate.Release();}
    }

    private async Task DownloadAsync(string link,string target,CancellationToken cancellation)
    {
        using var response=await http.GetAsync(link,HttpCompletionOption.ResponseHeadersRead,cancellation);
        if(!response.IsSuccessStatusCode)throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        var temporary=target+".download";
        try
        {
            await using(var source=await response.Content.ReadAsStreamAsync(cancellation))
            await using(var file=File.Create(temporary))
                await source.CopyToAsync(file,cancellation);
            File.Move(temporary,target,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    public void Dispose(){http.Dispose();gate.Dispose();}
}
