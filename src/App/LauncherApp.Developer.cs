using System.Globalization;
using System.Text.Json;
using Momoko.Launcher.Core;
using Momoko.Launcher.Games;
using Momoko.Launcher.Host;
using Momoko.Launcher.Services;

namespace Momoko.Launcher.App;

// Developer-build features. They are compiled only with the DEV symbol
// (dotnet publish -p:Edition=Dev); the user build gets the fixed behaviour in
// the #else branch: Momoko and server origins from launcher.json and Momoko,
// passport login only, no client console, default font, no extra arguments.
internal sealed partial class LauncherApp
{
#if DEV
    private const string SdkCredential="Momoko.Launcher.Dev/SDK/";
    private readonly Queue<UiLog> developerLog=new();
    private string developerStatus="就绪",developerPid="";

    private string? DeveloperMomoko()=>string.IsNullOrWhiteSpace(settings.Developer.Momoko)?null:settings.Developer.Momoko;

    private void AttachDeveloperLog()
    {
        LauncherLog.Written+=(time,level,text)=>UiDispatcher.Instance.Run(()=>
        {
            var line=new UiLog("log",time.ToString("HH:mm:ss",CultureInfo.InvariantCulture),level.ToString().ToLowerInvariant(),text);
            developerLog.Enqueue(line);
            while(developerLog.Count>400)developerLog.Dequeue();
            window.Post(JsonSerializer.Serialize(line,LauncherJson.Default.UiLog));
        });
    }

    private bool OnDeveloperMessage(string type,JsonElement root)
    {
        switch(type)
        {
            case "dev.replay":
                foreach(var line in developerLog)window.Post(JsonSerializer.Serialize(line,LauncherJson.Default.UiLog));
                return true;
            case "dev.momoko":
                Guard(()=>
                {
                    var value=Text(root,"value").Trim();
                    settings.Developer.Momoko=value.Length==0?null:Origins.Normalize(value,"Momoko 地址");
                    SaveSettings();
                    CancelAuthorization();accessToken=null;refreshToken=null;apps=[];settings.Account=null;
                    momoko.SetOrigin(MomokoOrigin);
                    LauncherLog.Write("Momoko 地址："+MomokoOrigin);
                    Run(RestoreAsync);
                });
                return true;
            case "dev.endpoints":
                Guard(()=>
                {
                    var server=DeveloperServerOf(Text(root,"id"));
                    server.Sdk=Optional(Text(root,"sdk"),"SDK 地址");
                    server.Game=Optional(Text(root,"game"),"游戏服务端地址");
                    server.Resource=Optional(Text(root,"resource"),"资源服务器地址");
                    SaveSettings();Publish();
                });
                return true;
            case "dev.loginMethod":
                Guard(()=>
                {
                    var value=Text(root,"value");
                    if(value is not ("passport" or "password"))return;
                    DeveloperServerOf(Text(root,"id")).LoginMethod=value;
                    SaveSettings();Publish();
                });
                return true;
            case "dev.password":
                Guard(()=>
                {
                    var id=Text(root,"id");
                    var account=Text(root,"account").Trim();
                    var password=Text(root,"password");
                    if(account.Length==0||password.Length==0)throw new InvalidDataException("请填写游戏账号和密码。");
                    var endpoints=EndpointsOf(Server(id));
                    CredentialStore.Save(SdkCredential+endpoints.Sdk,account,password,"Momoko 启动器开发版的游戏账号密码");
                    DeveloperServerOf(id).Account=account;
                    SaveSettings();
                    LauncherLog.Write($"已保存 {endpoints.Sdk} 的测试账号 {account}");
                    Publish();
                });
                return true;
            case "dev.console":
                settings.Developer.ClientConsole=root.TryGetProperty("value",out var console)&&console.ValueKind==JsonValueKind.True;
                SaveSettings();Publish();
                return true;
            case "dev.font":
                settings.Developer.Font=Text(root,"value").Trim();
                SaveSettings();Publish();
                return true;
            case "dev.args":
                settings.Developer.Arguments=Text(root,"value");
                SaveSettings();Publish();
                return true;
            case "dev.loginOnly":
            {
                var id=Text(root,"id");
                Run(()=>LaunchAsync(id,false));
                return true;
            }
            case "dev.cancel":
                operation?.Cancel();
                return true;
        }
        return false;
    }

    private static string? Optional(string value,string field)=>string.IsNullOrWhiteSpace(value)?null:Origins.Normalize(value,field);

    private DeveloperServer DeveloperServerOf(string id)
    {
        if(string.IsNullOrEmpty(id))throw new InvalidOperationException("缺少服务器 ID。");
        if(!settings.Developer.Servers.TryGetValue(id,out var server))settings.Developer.Servers[id]=server=new DeveloperServer();
        return server;
    }

    private GameEndpoints DeveloperEndpoints(string id,GameEndpoints endpoints)
    {
        if(!settings.Developer.Servers.TryGetValue(id,out var server))return endpoints;
        return new GameEndpoints(server.Sdk??endpoints.Sdk,server.Game??endpoints.Game,server.Resource??endpoints.Resource);
    }

    private async Task<SdkSession?> DeveloperSessionAsync(MomokoLaunchApp server,GameEndpoints endpoints,CancellationToken cancellation)
    {
        if(!settings.Developer.Servers.TryGetValue(server.AppId,out var developer)||developer.LoginMethod!="password")return null;
        var stored=CredentialStore.Load(SdkCredential+endpoints.Sdk)
            ?? throw new InvalidOperationException("开发版：请先在设置里保存这台服务器的游戏账号和密码。");
        LauncherLog.Write($"[{server.Server.Game}] 正在用游戏账号密码登录 SDK… {endpoints.Sdk} 账号={stored.UserName}");
        return await sdk.PasswordLoginAsync(endpoints.Sdk,stored.UserName,stored.Secret,cancellation);
    }

    private void SetDeveloperStatus(string status,string pid){developerStatus=status;developerPid=pid;}

    private UiDeveloper DeveloperState()=>new(MomokoOrigin,builtInMomoko,settings.Developer.ClientConsole,settings.Developer.Font,
        settings.Developer.Arguments,developerStatus,developerPid);

    private UiEndpoints DeveloperEndpointsState(MomokoLaunchApp server)
    {
        var effective=EndpointsOf(server);
        return new UiEndpoints(effective.Sdk,effective.Game,effective.Resource,server.Endpoints.Sdk,server.Endpoints.Game,server.Endpoints.Resource);
    }

    private UiDeveloperServer DeveloperServerState(MomokoLaunchApp server)
    {
        settings.Developer.Servers.TryGetValue(server.AppId,out var developer);
        var saved=false;
        try{saved=CredentialStore.Load(SdkCredential+EndpointsOf(server).Sdk) is not null;}
        catch(Exception error){LauncherLog.Warn("读取测试账号失败："+error.Message);}
        return new UiDeveloperServer(developer?.LoginMethod??"passport",developer?.Account??"",saved);
    }

    private string LocalizationFont()=>Path.GetFullPath(string.IsNullOrWhiteSpace(settings.Developer.Font)?DefaultFont:settings.Developer.Font);
    private bool ClientConsole()=>settings.Developer.ClientConsole;
    private IReadOnlyList<string> LaunchArguments()=>settings.Developer.Arguments.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
#else
    private static string? DeveloperMomoko()=>null;
    private static void AttachDeveloperLog(){}
    private static bool OnDeveloperMessage(string type,JsonElement root)=>false;
    private static GameEndpoints DeveloperEndpoints(string id,GameEndpoints endpoints)=>endpoints;
    private static Task<SdkSession?> DeveloperSessionAsync(MomokoLaunchApp server,GameEndpoints endpoints,CancellationToken cancellation)
        =>Task.FromResult<SdkSession?>(null);
    private static void SetDeveloperStatus(string status,string pid){}
    private static UiDeveloper? DeveloperState()=>null;
    private static UiEndpoints? DeveloperEndpointsState(MomokoLaunchApp server)=>null;
    private static UiDeveloperServer? DeveloperServerState(MomokoLaunchApp server)=>null;
    private static string LocalizationFont()=>DefaultFont;
    private static bool ClientConsole()=>false;
    private static IReadOnlyList<string> LaunchArguments()=>[];
#endif
    private const string DefaultFont=@"C:\Windows\Fonts\msyh.ttc";
}
