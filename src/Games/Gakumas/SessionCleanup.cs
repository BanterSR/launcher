using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Momoko.Launcher.Core;

namespace Momoko.Launcher.Games.Gakumas;

// A launch leaves two things DMM GAMES Player would pick up: version.dll next
// to the game, and the RefreshTokenLocalSaveData save holding the local SDK
// refresh token. After every game process from the game folder has exited,
// exactly these are removed; every other client save and cache stays.
// session.json marks a session whose cleanup has not finished yet.
internal static partial class SessionCleanup
{
    private static readonly Guid LocalAppDataLow=new("A520A1A4-1780-4FF6-BD18-167343C5AF16");
    private const uint QueryLimitedInformation=0x1000;

    public static string MarkerPath=>Path.Combine(AppPaths.GameData(GakumasGame.GameId),"session.json");

    [LibraryImport("kernel32.dll",SetLastError=true)]
    private static partial SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)]bool inherit,int processId);

    [LibraryImport("kernel32.dll",SetLastError=true,EntryPoint="QueryFullProcessImageNameW")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,char* name,ref uint size);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folder,uint flags,nint token,out nint path);

    public static string? Pending()
    {
        if(!File.Exists(MarkerPath))return null;
        SessionMarker? marker;
        try{marker=JsonSerializer.Deserialize(File.ReadAllText(MarkerPath),LauncherJson.Default.SessionMarker);}
        catch(JsonException error){throw new InvalidDataException("启动会话记录无效："+MarkerPath,error);}
        if(string.IsNullOrWhiteSpace(marker?.GamePath))throw new InvalidDataException("启动会话记录缺少游戏路径："+MarkerPath);
        return marker.GamePath;
    }

    // Written before anything is installed, so a failed launch or a launcher
    // closed early still leaves a record for the next cleanup.
    public static void Begin(string gamePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
        var temporary=MarkerPath+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(new SessionMarker(Path.GetFullPath(gamePath)),LauncherJson.Default.SessionMarker));
        File.Move(temporary,MarkerPath,true);
    }

    public static bool GameRunning(string gamePath)=>RunningProgram(gamePath) is not null;

    // A process started from a program directly in the game folder, as
    // "<image> (PID n)", or null. The launcher is portable and may itself
    // live in the game folder; its own process never counts.
    public static string? RunningProgram(string gamePath)
    {
        var folder=Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(gamePath))!);
        if(!Directory.Exists(folder))return null;
        var self=Environment.ProcessId;
        foreach(var program in Directory.EnumerateFiles(folder,"*.exe"))
        {
            foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(program)))
            {
                using(process)
                {
                    if(process.Id==self)continue;
                    var image=ImagePath(process.Id);
                    if(image is not null&&string.Equals(Path.GetDirectoryName(image),folder,StringComparison.OrdinalIgnoreCase))
                        return $"{image}（PID {process.Id}）";
                }
            }
        }
        return null;
    }

    // Removes the residue of the pending session and its marker. With no
    // pending session nothing is touched.
    public static IReadOnlyList<string> Run()
    {
        var gamePath=Pending();
        if(gamePath is null)return [];
        var running=RunningProgram(gamePath);
        if(running is not null)throw new InvalidOperationException("游戏目录中的程序仍在运行，退出后才能清理启动残留："+running);
        var report=new List<string>{ProxyInstaller.Uninstall(gamePath)};
        var token=RefreshTokenSavePath(gamePath);
        if(File.Exists(token))
        {
            DeleteFile(token);
            report.Add("已删除登录 token 存档（RefreshTokenLocalSaveData）："+token);
        }
        else report.Add("登录 token 存档不存在，无需删除："+token);
        File.Delete(MarkerPath);
        report.Add("登录残留已清理，客户端其他存档与资源缓存保持不变。");
        return report;
    }

    // Save files are <persistentDataPath>\MD5("save")\MD5(category)\MD5(full
    // type name), each MD5 as uppercase hex; RefreshTokenLocalSaveData is a
    // UserSaveData, category "user".
    private static string RefreshTokenSavePath(string gamePath)=>Path.Combine(PersistentDataPath(gamePath),
        SaveName("save"),SaveName("user"),SaveName("Campus.Common.Account.RefreshTokenLocalSaveData"));
    private static string SaveName(string value)=>Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    // Unity stores persistentDataPath under LocalLow\<company>\<product> from
    // <game>_Data\app.info; "BANDAI NAMCO Entertainment Inc." is written as
    // "BANDAI NAMCO Entertainment Inc_".
    private static string PersistentDataPath(string gamePath)
    {
        var info=Path.Combine(Path.GetDirectoryName(gamePath)!,Path.GetFileNameWithoutExtension(gamePath)+"_Data","app.info");
        if(!File.Exists(info))throw new FileNotFoundException("找不到游戏的 app.info，无法定位客户端本地数据目录。",info);
        var lines=File.ReadAllLines(info);
        if(lines.Length<2||string.IsNullOrWhiteSpace(lines[0])||string.IsNullOrWhiteSpace(lines[1]))
            throw new InvalidDataException("app.info 缺少公司名或产品名："+info);
        return Path.Combine(KnownFolder(LocalAppDataLow),Segment(lines[0]),Segment(lines[1]));
    }

    private static string Segment(string name)
    {
        var invalid=Path.GetInvalidFileNameChars();
        var characters=name.Trim().Select(c=>invalid.Contains(c)?'_':c).ToArray();
        for(var i=characters.Length-1;i>=0&&characters[i] is '.' or ' ';i--)characters[i]='_';
        return new string(characters);
    }

    private static string KnownFolder(Guid id)
    {
        var result=SHGetKnownFolderPath(in id,0,0,out var path);
        try
        {
            if(result!=0)throw Marshal.GetExceptionForHR(result)!;
            return Marshal.PtrToStringUni(path)!;
        }
        finally{Marshal.FreeCoTaskMem(path);}
    }

    private static unsafe string? ImagePath(int processId)
    {
        using var handle=OpenProcess(QueryLimitedInformation,false,processId);
        if(handle.IsInvalid)return null;
        var buffer=new char[32768];var size=(uint)buffer.Length;
        fixed(char* name=buffer)
            return QueryFullProcessImageName(handle,0,name,ref size)?new string(buffer,0,(int)size):null;
    }

    // The save can stay locked for a moment after the game process exits.
    private static void DeleteFile(string path)
    {
        for(var attempt=1;;attempt++)
        {
            try
            {
                File.SetAttributes(path,FileAttributes.Normal);
                File.Delete(path);
                return;
            }
            catch(Exception error) when(error is FileNotFoundException or DirectoryNotFoundException){return;}
            catch(Exception error) when(attempt<10&&error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            {
                throw new IOException("无法删除登录 token 存档，请确认游戏已完全退出："+path+Environment.NewLine+error.Message,error);
            }
        }
    }
}

internal sealed record SessionMarker(string GamePath);
