using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoko.Launcher.Core;

namespace Momoko.Launcher.Games.Gakumas;

// Imports a localify-layout translation zip into games\gakumas\localization.
// The native hook reads only this local folder when the game starts.
internal static class LocalizationPackage
{
    public static string DirectoryPath=>Path.Combine(AppPaths.GameData(GakumasGame.GameId),"localization");
    private const string MarkerName=".import.json";
    private const long EntryLimit=512L*1024*1024;
    private const long TotalLimit=4L*1024*1024*1024;
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    private enum Kind{TextMap,Master,Script}

    // The marker is written last, so it exists only for a complete import.
    public static LocalizationInfo? Current()
    {
        var marker=Path.Combine(DirectoryPath,MarkerName);
        if(!File.Exists(marker))return null;
        try{return JsonSerializer.Deserialize(File.ReadAllText(marker),LauncherJson.Default.LocalizationInfo);}
        catch(JsonException){return null;}
    }

    public static LocalizationInfo RequireImported()=>Current()
        ?? throw new InvalidDataException("还没有导入汉化包。请在设置里导入汉化包，或关闭汉化。");

    public static LocalizationInfo Import(string zipPath,IProgress<string> progress,CancellationToken cancellation)
    {
        using var archive=ZipFile.OpenRead(zipPath);
        var root=FindRoot(archive)
            ?? throw new InvalidDataException("压缩包里没有汉化数据（localization.json、generic.json、genericTrans、masterTrans 或 resource）。");
        progress.Report($"汉化包根目录：{(root.Length==0?"/":root)}");
        var staging=DirectoryPath+".importing";
        if(Directory.Exists(staging))Directory.Delete(staging,true);
        Directory.CreateDirectory(staging);
        var stagingRoot=Path.GetFullPath(staging)+Path.DirectorySeparatorChar;
        var skipped=new List<string>();
        int files=0,ignored=0;long total=0;
        try
        {
            foreach(var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                var name=entry.FullName.Replace('\\','/');
                if(name.EndsWith('/'))continue;
                if(!name.StartsWith(root,StringComparison.Ordinal)){ignored++;continue;}
                var relative=name[root.Length..];
                var kind=Classify(relative);
                if(kind is null){ignored++;continue;}
                if(relative.Split('/').Any(segment=>segment.Length==0||segment is "." or ".."))
                {skipped.Add(relative+"：路径无效");continue;}
                if(entry.Length>EntryLimit||(total+=entry.Length)>TotalLimit)
                    throw new InvalidDataException("汉化包解压后过大："+relative);
                byte[] bytes;
                using(var input=entry.Open())using(var buffer=new MemoryStream((int)entry.Length))
                {input.CopyTo(buffer);bytes=buffer.ToArray();}
                var problem=Validate(kind.Value,bytes);
                if(problem is not null){skipped.Add(relative+"："+problem);continue;}
                var target=Path.GetFullPath(Path.Combine(staging,relative.Replace('/',Path.DirectorySeparatorChar)));
                if(!target.StartsWith(stagingRoot,StringComparison.OrdinalIgnoreCase))
                {skipped.Add(relative+"：路径越界");continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target,bytes);
                if(++files%500==0)progress.Report($"已导入 {files} 个汉化文件…");
            }
            if(files==0)throw new InvalidDataException("压缩包里没有可用的汉化文件。");
            var info=new LocalizationInfo(Path.GetFileName(zipPath),ReadVersion(archive,root),DateTime.Now,files,ignored,skipped.ToArray());
            File.WriteAllText(Path.Combine(staging,MarkerName),JsonSerializer.Serialize(info,LauncherJson.Default.LocalizationInfo));
            // Rename first: a folder still in use by a running game fails here and stays intact.
            var previous=DirectoryPath+".previous";
            if(Directory.Exists(previous))Directory.Delete(previous,true);
            if(Directory.Exists(DirectoryPath))Directory.Move(DirectoryPath,previous);
            Directory.Move(staging,DirectoryPath);
            if(Directory.Exists(previous))Directory.Delete(previous,true);
            progress.Report($"已导入汉化包 {info.Source}：{SHA256Hex(zipPath)}");
            return info;
        }
        finally
        {
            if(Directory.Exists(staging))Directory.Delete(staging,true);
        }
    }

    // Paths the native hook reads, relative to the localify root.
    private static Kind? Classify(string relative)
    {
        if(relative is "localization.json" or "generic.json")return Kind.TextMap;
        if(relative.StartsWith("genericTrans/",StringComparison.Ordinal)&&relative.EndsWith(".json",StringComparison.OrdinalIgnoreCase))return Kind.TextMap;
        var parts=relative.Split('/');
        if(parts.Length==2&&parts[0]=="masterTrans"&&parts[1].EndsWith(".json",StringComparison.OrdinalIgnoreCase)&&parts[1].Length>5)return Kind.Master;
        if(parts.Length==2&&parts[0]=="resource"&&parts[1].EndsWith(".txt",StringComparison.OrdinalIgnoreCase)&&parts[1].Length>4)return Kind.Script;
        return null;
    }

    // The most common directory in front of a localify path is the package root.
    private static string? FindRoot(ZipArchive archive)
    {
        var roots=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var entry in archive.Entries)
        {
            var name=entry.FullName.Replace('\\','/');
            string? root=null;
            foreach(var marker in new[]{"localization.json","generic.json"})
                if(name==marker||name.EndsWith("/"+marker,StringComparison.Ordinal))root=name[..^marker.Length];
            foreach(var folder in new[]{"genericTrans/","masterTrans/","resource/"})
            {
                if(root is not null)break;
                if(name.StartsWith(folder,StringComparison.Ordinal)){root="";break;}
                var at=name.IndexOf("/"+folder,StringComparison.Ordinal);
                if(at>=0)root=name[..(at+1)];
            }
            if(root is not null)roots[root]=roots.GetValueOrDefault(root)+1;
        }
        return roots.Count==0?null:roots.OrderByDescending(item=>item.Value).ThenBy(item=>item.Key.Length).First().Key;
    }

    // Mirrors what localization.cpp accepts, so a rejected file is reported here
    // instead of being skipped silently in the game.
    private static string? Validate(Kind kind,byte[] bytes)
    {
        if(kind==Kind.Script)
        {
            try{StrictUtf8.GetString(bytes);return null;}
            catch(DecoderFallbackException){return "不是 UTF-8 文本";}
        }
        try
        {
            using var document=JsonDocument.Parse(bytes);
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return "顶层不是对象";
            if(kind==Kind.TextMap)
                return root.EnumerateObject().All(item=>item.Value.ValueKind==JsonValueKind.String)?null:"不是“原文: 译文”字符串对象";
            if(!root.TryGetProperty("rules",out var rules)||rules.ValueKind!=JsonValueKind.Object||
                !rules.TryGetProperty("primaryKeys",out var keys)||keys.ValueKind!=JsonValueKind.Array||keys.GetArrayLength()==0||
                !keys.EnumerateArray().All(key=>key.ValueKind==JsonValueKind.String&&!string.IsNullOrEmpty(key.GetString()))||
                !keys.EnumerateArray().Any(key=>!key.GetString()!.Contains('.')))
                return "rules.primaryKeys 无效";
            if(!root.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Array||
                !data.EnumerateArray().All(row=>row.ValueKind==JsonValueKind.Object))
                return "data 不是对象数组";
            return null;
        }
        catch(JsonException){return "JSON 格式错误";}
    }

    private static string ReadVersion(ZipArchive archive,string root)
    {
        var parent=root.TrimEnd('/');
        parent=parent.Contains('/')?parent[..(parent.LastIndexOf('/')+1)]:"";
        foreach(var candidate in new[]{root+"version.txt",parent+"version.txt"})
        {
            var entry=archive.GetEntry(candidate);
            if(entry is null||entry.Length>256)continue;
            using var reader=new StreamReader(entry.Open(),Encoding.UTF8);
            var text=reader.ReadToEnd().Trim();
            if(text.Length>0)return text;
        }
        return "";
    }

    private static string SHA256Hex(string path)
    {
        using var stream=File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
