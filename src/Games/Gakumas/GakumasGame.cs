using System.Diagnostics;
using System.Security.Principal;

namespace Momoko.Launcher.Games.Gakumas;

// 学园偶像大师 (PC, DMM build). The launcher installs its version.dll next to
// gakumas.exe and starts the game the way DMM GAMES Player does: elevated,
// with /viewer_id, /onetime_token, /open_id and /pf_access_token.
internal sealed class GakumasGame:GameModule
{
    public const string GameId="gakumas";

    public override string Id=>GameId;
    public override string ExecutableName=>"gakumas.exe";
    public override string DefaultExecutable=>@"D:\game\gakumas\gakumas.exe";
    public override bool SupportsLocalization=>true;
    public override LocalizationInfo? Localization=>LocalizationPackage.Current();

    public override void ValidateExecutable(string path)
    {
        if(!string.Equals(Path.GetFileName(path),ExecutableName,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择游戏目录里的 "+ExecutableName+"。");
        if(!File.Exists(path))throw new FileNotFoundException("找不到游戏程序。",path);
        if(!File.Exists(Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)+"_Data","app.info")))
            throw new InvalidDataException("所选目录不是完整的游戏目录（缺少 gakumas_Data\\app.info）。");
    }

    public override LocalizationInfo ImportLocalization(string zipPath,IProgress<string> progress,CancellationToken cancellation)
        =>LocalizationPackage.Import(zipPath,progress,cancellation);

    public override string? PendingSession()=>SessionCleanup.Pending();
    public override bool IsRunning(string executablePath)=>SessionCleanup.GameRunning(executablePath);
    public override IReadOnlyList<string> Cleanup()=>SessionCleanup.Run();

    public override Task<int> LaunchAsync(GameLaunch launch,IProgress<string> progress,CancellationToken cancellation)
        =>Task.Run(()=>Launch(launch,progress,cancellation),cancellation);

    private static int Launch(GameLaunch launch,IProgress<string> progress,CancellationToken cancellation)
    {
        var path=launch.ExecutablePath;
        if(!File.Exists(path))throw new FileNotFoundException("找不到游戏程序，请在设置里重新选择。",path);
        if(launch.Localization)
        {
            var package=LocalizationPackage.RequireImported();
            if(!File.Exists(launch.LocalizationFont))throw new FileNotFoundException("找不到汉化字体文件。",launch.LocalizationFont);
            progress.Report($"汉化已启用：{(package.Version.Length>0?package.Version:package.Source)}，{package.Files} 个文件");
        }
        else progress.Report("汉化未启用，游戏使用原文。");
        // DMM GAMES Player launches gakumas.exe with isAdministrator=true; the
        // launcher manifest requires elevation and the game inherits the token.
        using(var identity=WindowsIdentity.GetCurrent())
        {
            if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("启动器没有以管理员身份运行。游戏需要与 DMM 相同的管理员权限，请以管理员身份打开启动器。");
        }
        // Cleanup after exit assumes the game folder runs only this session.
        var running=SessionCleanup.RunningProgram(path);
        if(running is not null)throw new InvalidOperationException("游戏目录中的程序正在运行："+running+"。请先关闭它，再从启动器启动。");
        foreach(var line in SessionCleanup.Run())progress.Report(line);
        cancellation.ThrowIfCancellationRequested();
        SessionCleanup.Begin(path);
        var proxy=ProxyInstaller.Install(path);progress.Report("启动代理已就绪："+proxy);
        var context=new LaunchContext(launch);
        var start=new ProcessStartInfo(path){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)!};
        context.ApplyTo(start);
        var nonce=Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        foreach(var value in new[]{"/viewer_id="+context.PlatformId,"/onetime_token="+nonce,
            "/open_id="+launch.Session.UserId,"/pf_access_token="+nonce})start.ArgumentList.Add(value);
        for(var i=0;i<launch.Arguments.Count;i++)
        {
            var value=launch.Arguments[i];
            if(value.Equals("-logFile",StringComparison.OrdinalIgnoreCase)){i++;continue;}
            if(value.Equals("-nolog",StringComparison.OrdinalIgnoreCase))continue;
            start.ArgumentList.Add(value);
        }
        start.ArgumentList.Add("-logFile");start.ArgumentList.Add(context.PlayerLogPath);
        cancellation.ThrowIfCancellationRequested();
        using var entry=Process.Start(start)??throw new InvalidOperationException("无法启动游戏。");
        progress.Report($"入口 PID={entry.Id}；version.dll 随 Unity 启动加载。");
        progress.Report("Unity Player 日志："+context.PlayerLogPath);
        return entry.Id;
    }
}
