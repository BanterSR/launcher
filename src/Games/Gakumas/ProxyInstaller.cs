using System.Security.Cryptography;
using System.Text.Json;
using Momoko.Launcher.Core;

namespace Momoko.Launcher.Games.Gakumas;

// version.dll (embedded in the launcher as games/gakumas/version.dll) is placed
// next to the game only for a launch session and is removed by SessionCleanup
// after the game exits. The install record lets cleanup tell the launcher's own
// file from another loader's.
internal static class ProxyInstaller
{
    private const string Product="Gakumas.Launcher";

    private const string HookResource="games/gakumas/version.dll";
    private static string Target(string gamePath)=>Path.Combine(Path.GetDirectoryName(gamePath)!,"version.dll");
    private static string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(file));}
    private static string Hash(byte[] data)=>Convert.ToHexStringLower(SHA256.HashData(data));
    private static ProxyInstallRecord? Record(string marker)
    {
        if(!File.Exists(marker))return null;
        try{return JsonSerializer.Deserialize(File.ReadAllText(marker),LauncherJson.Default.ProxyInstallRecord);}
        catch(JsonException){return null;}
    }

    public static string Install(string gamePath)
    {
        var target=Target(gamePath);
        var hook=Embedded.Require(HookResource);
        var marker=target+".gakumas.json";
        var expected=Hash(hook);
        if(File.Exists(target))
        {
            var actual=Hash(target);
            if(actual==expected)return target;
            var old=Record(marker);
            if(old?.Product!=Product||old.Sha256!=actual)
                throw new IOException("游戏目录已有其他 version.dll，未覆盖。请先处理与其他加载器的冲突："+target);
        }
        var temporary=target+".gakumas-new";
        try{File.WriteAllBytes(temporary,hook);File.Move(temporary,target,true);}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {throw new IOException("无法安装 version.dll，请确认游戏目录可写且游戏已关闭。"+Environment.NewLine+error.Message,error);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        File.WriteAllText(marker,JsonSerializer.Serialize(new ProxyInstallRecord(expected,Product),LauncherJson.Default.ProxyInstallRecord));
        return target;
    }

    // Deletes version.dll only when it is the launcher's own file: it matches
    // the install record or the launcher's embedded version.dll.
    public static string Uninstall(string gamePath)
    {
        var target=Target(gamePath);
        var marker=target+".gakumas.json";
        File.Delete(target+".gakumas-new");
        string result;
        if(!File.Exists(target))result="游戏目录没有 version.dll，无需删除："+target;
        else
        {
            var actual=Hash(target);
            var record=Record(marker);
            var owned=(record?.Product==Product&&record.Sha256==actual)||Hash(Embedded.Require(HookResource))==actual;
            if(owned)
            {
                try{File.Delete(target);}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException)
                {throw new IOException("无法删除游戏目录的 version.dll，请确认游戏已完全退出："+target+Environment.NewLine+error.Message,error);}
                result="已删除启动器安装的 version.dll："+target;
            }
            else result="游戏目录的 version.dll 不是启动器安装的，保留不动："+target;
        }
        File.Delete(marker);
        return result;
    }
}

internal sealed record ProxyInstallRecord(string Sha256,string Product);
