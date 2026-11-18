using System.Globalization;
using System.Text.Json;
using Momoko.Launcher.Core;
using Momoko.Launcher.Games;
using Momoko.Launcher.Games.Gakumas;
using Momoko.Launcher.Host;
using Momoko.Launcher.Services;

namespace Momoko.Launcher.App;

// Owns the launcher state: Momoko passport session, launchable apps, game
// runs and settings. ui/app.js sends actions; every change is published back
// as one state message.
internal sealed partial class LauncherApp
{
#if DEV
    public const bool Developer=true;
#else
    public const bool Developer=false;
#endif
    private const string PassportCredential="Momoko.Launcher/Passport/";
    private static string DeviceName=>Developer?"Momoko 启动器 Dev (Windows)":"Momoko 启动器 (Windows)";

    private readonly HostWindow window;
    private readonly string builtInMomoko;
    private readonly LauncherSettings settings;
    private readonly MomokoClient momoko;
    private readonly SdkClient sdk=new();
    private readonly UpdateClient updates=new();
    private readonly MediaCache media=new();
    private readonly GameModule[] modules=[new GakumasGame()];
    private readonly SemaphoreSlim tokenGate=new(1,1);
    private readonly Dictionary<string,GameRun> runs=new(StringComparer.Ordinal);

    private string phase="starting";
    private string? notice;
    private string? accessToken;
    private DateTime accessExpires;
    private string? refreshToken;
    private ClientAuthorization? authorization;
    private CancellationTokenSource? authorizing;
    private bool loginBusy;
    private string? loginError;
    private MomokoLaunchApp[] apps=[];
    private CancellationTokenSource? operation;
    private bool closing;
    private bool updateStarted;
    private bool updateChecking;
    private string? updateError;
    private LauncherRelease? newerRelease;
    private Version? updatePrompted;
    private bool updateInstalling;
    private int updateProgress=-1;

    // A browser authorization in progress (docs/login-protocol.md section 1.1).
    private sealed record ClientAuthorization(string DeviceCode,string UserCode,string VerificationUrl,int Interval,DateTime Expires);

    private sealed class GameRun
    {
        public string Status="idle";
        public string? AppId;
        public bool Watching;
    }

    public LauncherApp(HostWindow window,string builtInMomoko,LauncherSettings settings)
    {
        this.window=window;this.builtInMomoko=builtInMomoko;this.settings=settings;
        momoko=new MomokoClient(MomokoOrigin);
    }

    private string MomokoOrigin=>DeveloperMomoko()??builtInMomoko;
    private string CredentialTarget=>PassportCredential+MomokoOrigin;

    public void Start()
    {
        window.MessageReceived+=OnMessage;
        window.CloseRequested+=OnCloseRequested;
        window.ExitRequested+=Exit;
        window.TrayItems=TrayMenu;
        window.TrayCommand+=OnTrayCommand;
        window.Failed+=message=>{LauncherLog.Error(message);Dialogs.Error(window.Handle,message);window.Destroy();};
        AttachDeveloperLog();
        foreach(var module in modules)WatchCleanup(module);
    }

    // ---------------------------------------------------------------- messages

    private void OnMessage(string json)
    {
        JsonDocument document;
        try{document=JsonDocument.Parse(json);}
        catch(JsonException){LauncherLog.Warn("界面消息无法解析："+json);return;}
        using(document)
        {
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return;
            var type=Text(root,"type");
            switch(type)
            {
                case "ready":
                    Publish();Run(RestoreAsync);
                    if(!updateStarted){updateStarted=true;Run(()=>CheckUpdateAsync(false));}
                    break;
                case "update.check":Run(()=>CheckUpdateAsync(true));break;
                case "update.open":HostWindow.OpenExternal(newerRelease?.Page??UpdateClient.ReleasesPage);break;
                case "update.install":Run(InstallUpdateAsync);break;
                case "window.minimize":window.Minimize();break;
                case "window.close":OnCloseRequested();break;
                case "window.closeChoice":
                {
                    var action=Text(root,"action");
                    var remember=root.TryGetProperty("remember",out var value)&&value.ValueKind==JsonValueKind.True;
                    CloseWith(action,remember);
                    break;
                }
                case "close.set":SetCloseAction(Text(root,"value"));break;
                case "window.drag":window.BeginDrag();break;
                case "theme.set":SetTheme(Text(root,"value"));break;
                case "theme.system":if(settings.Theme=="system")window.SetBackground(root.TryGetProperty("dark",out var dark)&&dark.ValueKind==JsonValueKind.True);break;
                case "auth.start":Run(StartAuthorizationAsync);break;
                case "auth.reopen":if(authorization is not null)HostWindow.OpenExternal(authorization.VerificationUrl);break;
                case "auth.cancel":CancelAuthorization();loginError=null;Publish();break;
                case "auth.logout":Run(LogoutAsync);break;
                case "auth.retry":Run(RestoreAsync);break;
                case "open.register":HostWindow.OpenExternal(momoko.PageUrl("/register"));break;
                case "open.forgot":HostWindow.OpenExternal(momoko.PageUrl("/forgot"));break;
                case "open.account":HostWindow.OpenExternal(momoko.PageUrl("/account"));break;
                // The passport's game space for a server (passport web /games/{serverId}).
                case "open.gameSpace":{var id=Text(root,"id");Guard(()=>HostWindow.OpenExternal(momoko.PageUrl("/games/"+Uri.EscapeDataString(Server(id).Server.Id))));break;}
                case "open.logs":HostWindow.OpenFolder(AppPaths.Logs);break;
                case "server.select":settings.Selected=Text(root,"id");SaveSettings();Publish();break;
                case "server.refresh":Run(RefreshServersAsync);break;
                case "server.launch":{var id=Text(root,"id");Run(()=>LaunchAsync(id,true));break;}
                case "server.bind":{var id=Text(root,"id");var account=Text(root,"account");var password=Text(root,"password");Run(()=>BindAsync(id,account,password));break;}
                case "server.create":{var id=Text(root,"id");Run(()=>CreateAccountAsync(id));break;}
                case "game.browse":Guard(()=>BrowseExecutable(Text(root,"game")));break;
                case "game.localization":{var game=Text(root,"game");var enabled=root.TryGetProperty("enabled",out var value)&&value.ValueKind==JsonValueKind.True;Guard(()=>SetLocalization(game,enabled));break;}
                case "game.importLocalization":{var game=Text(root,"game");Run(()=>ImportLocalizationAsync(game));break;}
                case "game.freeResolution":{var game=Text(root,"game");var enabled=root.TryGetProperty("enabled",out var value)&&value.ValueKind==JsonValueKind.True;Guard(()=>SetFreeResolution(game,enabled));break;}
                default:
                    if(!OnDeveloperMessage(type,root))LauncherLog.Warn("未知的界面消息："+type);
                    break;
            }
        }
    }

    private static string Text(JsonElement root,string name)
        =>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";

    private async void Run(Func<Task> action)
    {
        try{await action();}
        catch(Exception error){Report(error);}
    }

    private void Guard(Action action)
    {
        try{action();}
        catch(Exception error){Report(error);}
    }

    private void Report(Exception error)
    {
        switch(error)
        {
            case MomokoException momokoError when momokoError.Unauthorized:
                LauncherLog.Warn("通行证登录已失效："+momokoError.Message);
                ForgetLogin();phase="login";loginError="登录已失效，请重新登录。";
                break;
            case MomokoException or MomokoUnavailableException or SdkException or UpdateUnavailableException or InvalidDataException
                or InvalidOperationException or FileNotFoundException or IOException or UnauthorizedAccessException or NotSupportedException:
                LauncherLog.Error(error.Message);
                break;
            default:
                LauncherLog.Error(error.ToString());
                break;
        }
        Toast("error",error.Message);
        Publish();
    }

    private void Toast(string kind,string text)=>window.Post(JsonSerializer.Serialize(new UiToast("toast",kind,text),LauncherJson.Default.UiToast));

    private void Signal(string type)=>window.Post(JsonSerializer.Serialize(new UiSignal(type),LauncherJson.Default.UiSignal));

    // ---------------------------------------------------------------- passport

    private async Task RestoreAsync()
    {
        phase="starting";notice=null;loginError=null;Publish();
        StoredSecret? saved=null;
        try{saved=CredentialStore.Load(CredentialTarget);}
        catch(Exception error){LauncherLog.Error("读取保存的通行证登录失败："+error.Message);}
        if(saved is null){phase="login";Publish();return;}
        refreshToken=saved.Secret;
        try
        {
            await RefreshTokenAsync(CancellationToken.None);
            await LoadAccountAsync(CancellationToken.None);
            phase="ready";
        }
        catch(MomokoException error) when(error.Unauthorized||error.Code is "PASSPORT_ACCOUNT_DISABLED" or "PASSPORT_USER_NOT_FOUND")
        {
            LauncherLog.Warn("保存的通行证登录已失效："+error.Message);
            ForgetLogin();phase="login";loginError=error.Message;
        }
        catch(MomokoUnavailableException error)
        {
            LauncherLog.Error(error.Message);
            phase="offline";notice=error.Message;
        }
        Publish();
    }

    // Browser authorization: Momoko hands out a device code and a user code, the
    // passport page in the system browser signs the player in with whatever
    // method the passport offers and approves this device, and the launcher
    // polls until the session is issued.
    private async Task StartAuthorizationAsync()
    {
        if(loginBusy||authorization is not null)return;
        loginBusy=true;loginError=null;Publish();
        ClientAuthorization pending;
        try
        {
            var started=await momoko.AuthorizeClientAsync(new MomokoClientAuthRequest("launcher",settings.DeviceId,DeviceName),CancellationToken.None);
            pending=new ClientAuthorization(started.DeviceCode,started.UserCode,AuthorizationPage(started.VerificationUrl),
                (int)Math.Clamp(started.Interval,1,30),DateTime.UtcNow.AddSeconds(Math.Max(30,started.ExpiresIn)));
        }
        catch(Exception error) when(error is MomokoException or MomokoUnavailableException)
        {
            loginError=error.Message;LauncherLog.Warn("发起通行证授权失败："+error.Message);
            return;
        }
        finally{loginBusy=false;Publish();}
        var cancel=new CancellationTokenSource();
        authorization=pending;authorizing=cancel;
        LauncherLog.Write($"已在浏览器中打开通行证授权页，验证码 {pending.UserCode}");
        HostWindow.OpenExternal(pending.VerificationUrl);
        Publish();
        try{await PollAuthorizationAsync(pending,cancel.Token);}
        finally
        {
            if(ReferenceEquals(authorizing,cancel)){authorizing=null;authorization=null;}
            cancel.Dispose();
            Publish();
        }
    }

    private string AuthorizationPage(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var page)||page.Scheme is not ("https" or "http")||
           !string.Equals(page.Host,new Uri(MomokoOrigin).Host,StringComparison.OrdinalIgnoreCase))
            throw new MomokoUnavailableException("Momoko 通行证返回的授权页地址无效："+url);
        return page.AbsoluteUri;
    }

    private async Task PollAuthorizationAsync(ClientAuthorization pending,CancellationToken cancellation)
    {
        var interval=pending.Interval;
        MomokoTokens tokens;
        while(true)
        {
            try{await Task.Delay(TimeSpan.FromSeconds(interval),cancellation);}
            catch(OperationCanceledException){return;}
            if(DateTime.UtcNow>=pending.Expires){loginError="授权已过期，请重新登录。";return;}
            try{tokens=await momoko.ClientTokenAsync(pending.DeviceCode,cancellation);break;}
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested){return;}
            catch(MomokoException error) when(error.Code=="PASSPORT_AUTH_PENDING"){}
            catch(MomokoException error) when(error.Code=="PASSPORT_AUTH_SLOW_DOWN"){interval+=2;}
            catch(MomokoUnavailableException error){LauncherLog.Warn("等待授权时连接 Momoko 失败，稍后重试："+error.Message);}
            catch(MomokoException error)
            {
                loginError=error.Message;LauncherLog.Warn("通行证授权结束："+error.Message);
                return;
            }
        }
        try
        {
            await CompleteLoginAsync(tokens);
            window.Attention();
        }
        catch(Exception error) when(error is MomokoException or MomokoUnavailableException)
        {
            ForgetLogin();phase="login";loginError=error.Message;
            LauncherLog.Warn("通行证登录失败："+error.Message);
        }
    }

    private void CancelAuthorization()
    {
        authorizing?.Cancel();
        authorizing=null;authorization=null;
    }

    private async Task CompleteLoginAsync(MomokoTokens result)
    {
        ApplyTokens(result);
        loginError=null;
        await LoadAccountAsync(CancellationToken.None);
        phase="ready";
        LauncherLog.Ok("通行证登录成功："+settings.Account?.Username);
    }

    private void ApplyTokens(MomokoTokens result)
    {
        if(string.IsNullOrEmpty(result.AccessToken)||string.IsNullOrEmpty(result.RefreshToken))
            throw new MomokoUnavailableException("Momoko 通行证没有返回登录令牌。");
        accessToken=result.AccessToken;
        accessExpires=DateTime.UtcNow.AddSeconds(Math.Max(60,result.ExpiresIn)-60);
        refreshToken=result.RefreshToken;
        CredentialStore.Save(CredentialTarget,settings.Account?.Username??"",refreshToken,"Momoko 启动器保存的通行证登录");
    }

    // Refresh rotates both tokens; the new refresh token replaces the saved one.
    private async Task RefreshTokenAsync(CancellationToken cancellation)
    {
        await tokenGate.WaitAsync(cancellation);
        try
        {
            if(accessToken is not null&&DateTime.UtcNow<accessExpires)return;
            if(refreshToken is null)throw new MomokoException(System.Net.HttpStatusCode.Unauthorized,"PASSPORT_UNAUTHORIZED","请先登录通行证。");
            ApplyTokens(await momoko.RefreshAsync(refreshToken,cancellation));
        }
        finally{tokenGate.Release();}
    }

    private async Task<string> AccessAsync(CancellationToken cancellation)
    {
        await RefreshTokenAsync(cancellation);
        return accessToken!;
    }

    private async Task LoadAccountAsync(CancellationToken cancellation)
    {
        var me=await momoko.MeAsync(await AccessAsync(cancellation),cancellation);
        settings.Account=new AccountProfile
        {
            Id=me.Id,
            Username=me.Username,
            Name=string.IsNullOrWhiteSpace(me.Name)?me.Username:me.Name!,
            Avatar=me.Avatar??"",
            Email=me.Email??"",
            EmailVerified=me.EmailVerified,
            TotpEnabled=me.TotpEnabled,
            Joined=me.CreateTime,
            LastLogin=me.LastLoginAt
        };
        SaveSettings();
        await LoadServersAsync(cancellation);
    }

    private async Task LoadServersAsync(CancellationToken cancellation)
    {
        var list=await momoko.AppsAsync(await AccessAsync(cancellation),cancellation);
        apps=list.Apps.Where(server=>ModuleOf(server.Server.Game) is not null).ToArray();
        if(settings.Selected is null||apps.All(app=>app.AppId!=settings.Selected))
            settings.Selected=apps.FirstOrDefault()?.AppId;
        SaveSettings();
        LauncherLog.Write($"可启动的服务器：{apps.Length} 台");
        SyncMedia();
    }

    // Game icons and backgrounds are shown from the local cache; links that
    // are new or changed download in the background and the page updates as
    // each file arrives (Services.MediaCache).
    private async void SyncMedia()
    {
        var links=apps.SelectMany(app=>new[]{app.Icon,app.Background}).ToArray();
        try{await media.SyncAsync(links,Publish,CancellationToken.None);}
        catch(Exception error){Report(error);}
    }

    private async Task RefreshServersAsync()
    {
        await LoadServersAsync(CancellationToken.None);
        Publish();
    }

    private async Task LogoutAsync()
    {
        var token=accessToken;
        ForgetLogin();
        phase="login";
        Publish();
        if(token is null)return;
        try{await momoko.LogoutAsync(token,CancellationToken.None);}
        catch(Exception error) when(error is MomokoException or MomokoUnavailableException){LauncherLog.Warn("通行证退出请求失败："+error.Message);}
        LauncherLog.Write("已退出通行证登录。");
    }

    private void ForgetLogin()
    {
        CancelAuthorization();accessToken=null;refreshToken=null;apps=[];
        try{CredentialStore.Delete(CredentialTarget);}
        catch(Exception error){LauncherLog.Error("删除保存的通行证登录失败："+error.Message);}
        settings.Account=null;
        SaveSettings();
    }

    // ---------------------------------------------------------------- launch targets (client apps)

    private GameModule? ModuleOf(string game)=>modules.FirstOrDefault(module=>module.Id==game);

    private MomokoLaunchApp Server(string id)=>apps.FirstOrDefault(app=>app.AppId==id)
        ?? throw new InvalidOperationException("找不到这台游戏服务器，请刷新列表。");

    private GameRun RunOf(string module)
    {
        if(!runs.TryGetValue(module,out var run))runs[module]=run=new GameRun();
        return run;
    }

    private async Task BindAsync(string id,string account,string password)
    {
        account=account.Trim();
        if(account.Length==0||password.Length==0)throw new InvalidDataException("请输入游戏账号和密码。");
        var link=await momoko.BindAsync(await AccessAsync(CancellationToken.None),id,new MomokoLaunchBindRequest(account,password),CancellationToken.None);
        LauncherLog.Ok($"已绑定游戏账号：应用 {id}，玩家 {link.PlayerId}");
        await LoadServersAsync(CancellationToken.None);
        Toast("ok","已绑定游戏账号。");
        Publish();
    }

    private async Task CreateAccountAsync(string id)
    {
        var link=await momoko.CreateAccountAsync(await AccessAsync(CancellationToken.None),id,CancellationToken.None);
        LauncherLog.Ok($"已创建游戏账号：应用 {id}，玩家 {link.PlayerId}");
        await LoadServersAsync(CancellationToken.None);
        Toast("ok","已创建新的游戏账号。");
        Publish();
    }

    private GameEndpoints EndpointsOf(MomokoLaunchApp server)
    {
        var endpoints=new GameEndpoints(server.Endpoints.Sdk,server.Endpoints.Game,server.Endpoints.Resource);
        return DeveloperEndpoints(server.AppId,endpoints);
    }

    private async Task LaunchAsync(string id,bool startGame)
    {
        if(operation is not null)return;
        var server=Server(id);
        var module=ModuleOf(server.Server.Game)!;
        var run=RunOf(module.Id);
        if(run.Status!="idle")throw new InvalidOperationException("游戏正在运行或清理中，请等游戏退出。");
        var game=settings.Game(module.Id);
        var executable=game.Executable??module.DefaultExecutable;
        if(startGame)module.ValidateExecutable(executable);
        var endpoints=EndpointsOf(server);
        operation=new CancellationTokenSource();
        var cancellation=operation.Token;
        run.Status="launching";run.AppId=id;
        SetDeveloperStatus(startGame?"正在启动…":"正在登录…","");
        Publish();
        try
        {
            var session=await SessionAsync(server,endpoints,cancellation);
            LauncherLog.Ok($"[{module.Id}] 游戏服务器登录成功：玩家 {session.PlayerId}，user {session.UserId}");
            if(!startGame){run.Status="idle";run.AppId=null;SetDeveloperStatus("登录成功","");return;}
            var progress=new Progress<string>(line=>LauncherLog.Write($"[{module.Id}] {line}"));
            var launch=new GameLaunch(executable,endpoints,session,module.SupportsLocalization&&game.Localization,
                LocalizationFont(),module.SupportsFreeResolution&&game.FreeResolution,ClientConsole(),LaunchArguments());
            var pid=await module.LaunchAsync(launch,progress,cancellation);
            run.Status="running";
            SetDeveloperStatus("游戏运行中",pid.ToString(CultureInfo.InvariantCulture));
            LauncherLog.Ok($"[{module.Id}] 游戏已启动，入口 PID={pid}");
            WatchCleanup(module);
        }
        catch(OperationCanceledException)
        {
            LauncherLog.Warn("已取消当前操作。");
            run.Status="idle";run.AppId=null;SetDeveloperStatus("已取消","");
            WatchCleanup(module);
        }
        catch
        {
            run.Status="idle";run.AppId=null;SetDeveloperStatus("操作失败","");
            WatchCleanup(module);
            throw;
        }
        finally
        {
            operation?.Dispose();operation=null;
            Publish();
        }
    }

    private async Task<SdkSession> SessionAsync(MomokoLaunchApp server,GameEndpoints endpoints,CancellationToken cancellation)
    {
        var developerSession=await DeveloperSessionAsync(server,endpoints,cancellation);
        if(developerSession is not null)return developerSession;
        if(server.Link is null)throw new InvalidOperationException("请先绑定或创建这台服务器的游戏账号。");
        var token=await AccessAsync(cancellation);
        LauncherLog.Write($"[{server.Server.Game}] 正在申请启动票据… 应用 {server.AppId}，服务器 {server.Server.Id}");
        var ticket=await momoko.TicketAsync(token,server.AppId,cancellation);
        LauncherLog.Write($"[{server.Server.Game}] 正在用启动票据登录游戏服务器… {endpoints.Sdk}");
        return await sdk.TicketLoginAsync(endpoints.Sdk,ticket.Ticket,cancellation);
    }

    // After a launch, and at startup for a session left by an earlier run:
    // wait until no process from the game folder runs, then remove the residue.
    private async void WatchCleanup(GameModule module)
    {
        var run=RunOf(module.Id);
        if(run.Watching)return;
        string? path;
        try{path=module.PendingSession();}
        catch(Exception error){Report(error);return;}
        if(path is null)return;
        run.Watching=true;
        try
        {
            while(await Task.Run(()=>module.IsRunning(path)))
            {
                if(run.Status!="running"){run.Status="running";Publish();}
                await Task.Delay(1000);
            }
            run.Status="cleaning";Publish();
            var report=await Task.Run(module.Cleanup);
            foreach(var line in report)LauncherLog.Write($"[{module.Id}] {line}");
            LauncherLog.Ok($"[{module.Id}] 游戏已退出，登录残留已清理。");
            SetDeveloperStatus("游戏已退出 · 残留已清理","");
        }
        catch(Exception error){Report(error);}
        finally
        {
            run.Watching=false;run.Status="idle";run.AppId=null;
            Publish();
            if(closing)TryClose();
        }
    }

    // ---------------------------------------------------------------- updates

    // The user build compares its version with the latest GitHub release when
    // the page is ready, and again whenever the player asks. A newer release
    // opens the update dialog: once per version after the automatic check,
    // every time after a manual one. Only the user build is released; the
    // developer build and local builds never check.
    private async Task CheckUpdateAsync(bool manual)
    {
        if(updateChecking||updateInstalling)return;
        if(!UpdateEnabled)
        {
            if(manual)Toast("info",Developer?"开发版不检查更新。":"本地构建没有版本号，不检查更新。");
            return;
        }
        updateChecking=true;updateError=null;Publish();
        try
        {
            var release=await updates.LatestAsync(CancellationToken.None);
            if(release.Version>UpdateClient.Current)
            {
                if(newerRelease?.Version!=release.Version)
                    LauncherLog.Write($"发现新版本 {release.Tag}（当前 {BuildInfo.Version}）：{release.Page}");
                newerRelease=release;
                if(manual||updatePrompted!=release.Version)
                {
                    updatePrompted=release.Version;
                    Signal("update.prompt");
                }
            }
            else
            {
                newerRelease=null;
                if(manual)Toast("ok",$"已是最新版本（{BuildInfo.Version}）。");
            }
        }
        catch(UpdateUnavailableException error)
        {
            updateError=error.Message;
            LauncherLog.Warn("检查更新失败："+error.Message);
            if(manual)Toast("error","检查更新失败："+error.Message);
        }
        finally
        {
            updateChecking=false;
            Publish();
        }
    }

    // One click: download and check the release executable, put it in place
    // of this one and restart (Services.UpdateClient). Not while a game runs
    // or starts, so the restart never interrupts a launch.
    private async Task InstallUpdateAsync()
    {
        if(updateInstalling)return;
        var release=newerRelease??throw new InvalidOperationException("没有可安装的新版本。");
        var asset=release.Asset??throw new InvalidOperationException("这个版本没有可自动安装的启动器文件，请前往 GitHub 下载。");
        if(UpdateBlocked() is {} reason)throw new InvalidOperationException(reason);
        updateInstalling=true;updateProgress=0;Publish();
        try
        {
            LauncherLog.Write($"正在下载新版本 {release.Tag}：{asset.Url}");
            var progress=new Progress<int>(percent=>{updateProgress=percent;Publish();});
            var staged=await updates.DownloadAsync(asset,progress,CancellationToken.None);
            if(UpdateBlocked() is {} late)
            {
                File.Delete(staged);
                throw new InvalidOperationException(late);
            }
            UpdateClient.Install(staged);
            LauncherLog.Ok($"新版本 {release.Tag} 已安装，正在重启启动器。");
        }
        catch
        {
            updateInstalling=false;updateProgress=-1;Publish();
            throw;
        }
        try{UpdateClient.Restart();}
        catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            updateInstalling=false;updateProgress=-1;newerRelease=null;Publish();
            throw new InvalidOperationException($"新版本 {release.Tag} 已安装，但无法自动重启（{error.Message}），请手动重新打开启动器。",error);
        }
        Shutdown();
    }

    private string? UpdateBlocked()=>operation is not null||runs.Values.Any(run=>run.Status!="idle")
        ?"游戏正在启动、运行或清理，退出游戏后再更新。":null;

    private static bool UpdateEnabled=>!Developer&&UpdateClient.Current is not null;

    private UiUpdate UpdateState()=>new(BuildInfo.Version,UpdateEnabled,updateChecking,
        newerRelease?.Tag,newerRelease?.Page,newerRelease?.Notes,newerRelease?.Asset is not null,
        updateInstalling,updateProgress,UpdateBlocked(),updateError);

    // ---------------------------------------------------------------- settings

    private void SetTheme(string value)
    {
        if(value is not ("system" or "light" or "dark"))return;
        settings.Theme=value;
        SaveSettings();
        window.SetBackground(value=="dark"||(value=="system"&&Dialogs.SystemDark()));
        Publish();
    }

    private void BrowseExecutable(string gameId)
    {
        var module=ModuleOf(gameId)??throw new InvalidOperationException("未知的游戏："+gameId);
        var game=settings.Game(module.Id);
        var path=Dialogs.OpenFile(window.Handle,"选择 "+module.ExecutableName,game.Executable??module.DefaultExecutable,
            $"游戏程序 ({module.ExecutableName})",module.ExecutableName);
        if(path is null)return;
        module.ValidateExecutable(path);
        game.Executable=Path.GetFullPath(path);
        SaveSettings();
        LauncherLog.Write($"[{module.Id}] 游戏程序：{game.Executable}");
        Publish();
    }

    private void SetLocalization(string gameId,bool enabled)
    {
        var module=ModuleOf(gameId)??throw new InvalidOperationException("未知的游戏："+gameId);
        if(!module.SupportsLocalization)return;
        if(enabled&&module.Localization is null)throw new InvalidDataException("请先导入汉化包，再开启汉化。");
        settings.Game(module.Id).Localization=enabled;
        SaveSettings();
        Publish();
    }

    // Read by the game hook at launch, so it changes only while the game is not running.
    private void SetFreeResolution(string gameId,bool enabled)
    {
        var module=ModuleOf(gameId)??throw new InvalidOperationException("未知的游戏："+gameId);
        if(!module.SupportsFreeResolution)return;
        if(RunOf(module.Id).Status!="idle")throw new InvalidOperationException("游戏运行中不能更改自由分辨率，请先退出游戏。");
        settings.Game(module.Id).FreeResolution=enabled;
        SaveSettings();
        LauncherLog.Write($"[{module.Id}] 自由分辨率：{(enabled?"开启":"关闭")}");
        Publish();
    }

    private async Task ImportLocalizationAsync(string gameId)
    {
        var module=ModuleOf(gameId)??throw new InvalidOperationException("未知的游戏："+gameId);
        if(RunOf(module.Id).Status!="idle")throw new InvalidOperationException("游戏运行中不能导入汉化包，请先退出游戏。");
        if(operation is not null)return;
        var path=Dialogs.OpenFile(window.Handle,"选择汉化压缩包","","汉化压缩包 (*.zip)","*.zip");
        if(path is null)return;
        operation=new CancellationTokenSource();
        var cancellation=operation.Token;
        try
        {
            LauncherLog.Write($"[{module.Id}] 正在导入汉化包：{path}");
            var progress=new Progress<string>(line=>LauncherLog.Write($"[{module.Id}] {line}"));
            var info=await Task.Run(()=>module.ImportLocalization(path,progress,cancellation),cancellation);
            foreach(var problem in info.Skipped)LauncherLog.Warn($"[{module.Id}] 未导入：{problem}");
            LauncherLog.Ok($"[{module.Id}] 汉化包导入完成：{info.Files} 个文件，忽略 {info.Ignored} 个，未通过校验 {info.Skipped.Length} 个");
            settings.Game(module.Id).Localization=true;
            SaveSettings();
            Toast("ok",info.Skipped.Length==0?$"汉化包已导入（{info.Files} 个文件）。":$"汉化包已导入，{info.Skipped.Length} 个文件未通过校验，详见日志。");
        }
        finally
        {
            operation?.Dispose();operation=null;
            Publish();
        }
    }

    private void SaveSettings()
    {
        try{settings.Save();}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){LauncherLog.Error("保存设置失败："+error.Message);}
    }

    // ---------------------------------------------------------------- window

    // Closing the window (the page's close button, Alt+F4, the taskbar) does
    // what the player chose: hide to the notification area, exit, or ask with
    // the close dialog. The tray icon's menu always offers both.
    private void OnCloseRequested()
    {
        switch(settings.CloseAction)
        {
            case "tray":window.HideToTray();break;
            case "exit":Exit();break;
            default:
                if(!window.Ready){Exit();break;}
                window.Restore();
                Signal("close.ask");
                break;
        }
    }

    private void CloseWith(string action,bool remember)
    {
        if(action is not ("tray" or "exit"))return;
        if(remember)
        {
            settings.CloseAction=action;
            SaveSettings();
            Publish();
        }
        if(action=="tray")window.HideToTray();
        else Exit();
    }

    private void SetCloseAction(string value)
    {
        if(value is not ("ask" or "tray" or "exit"))return;
        settings.CloseAction=value;
        SaveSettings();
        Publish();
    }

    // Notification area quick actions: start each server's game ("game
    // (server)", unavailable while it cannot start) and open the settings.
    private IReadOnlyList<TrayItem> TrayMenu()
    {
        var items=new List<TrayItem>();
        if(phase=="ready"&&apps.Length>0)
        {
            foreach(var server in apps)
            {
                var run=RunOf(ModuleOf(server.Server.Game)!.Id);
                var text=$"启动 {TitleOf(server)} ({server.Server.Name})";
                if(run.Status!="idle"&&run.AppId==server.AppId)text+=run.Status switch{"running"=>" · 运行中","cleaning"=>" · 清理中",_=>" · 启动中"};
                items.Add(new TrayItem(text,"launch:"+server.AppId,CanLaunch(server)));
            }
            items.Add(TrayItem.Separator);
        }
        items.Add(new TrayItem("打开设置","settings",true));
        return items;
    }

    private bool CanLaunch(MomokoLaunchApp server)=>operation is null&&RunOf(ModuleOf(server.Server.Game)!.Id).Status=="idle"&&
        (server.Link is not null||DeveloperServerState(server)?.LoginMethod=="password");

    private void OnTrayCommand(string command)
    {
        if(command=="settings")
        {
            window.ShowFromTray();
            Signal("settings.open");
            return;
        }
        if(!command.StartsWith("launch:",StringComparison.Ordinal))return;
        var id=command["launch:".Length..];
        settings.Selected=id;
        SaveSettings();
        Publish();
        // Started from the tray, the window may be hidden: a failure brings it back to show why.
        Run(async()=>
        {
            try{await LaunchAsync(id,true);}
            catch{window.Attention();throw;}
        });
    }

    // A game that is starting, running or being cleaned up keeps the launcher
    // alive until its cleanup is done.
    private void Exit()
    {
        if(operation is not null||runs.Values.Any(run=>run.Status!="idle"))
        {
            closing=true;
            window.Minimize();
            Toast("info","游戏退出并清理完成后，启动器会自动关闭。");
            return;
        }
        Shutdown();
    }

    private void TryClose()
    {
        if(closing&&operation is null&&runs.Values.All(run=>run.Status=="idle"))Shutdown();
    }

    private void Shutdown()
    {
        SaveSettings();
        window.Destroy();
    }

    // ---------------------------------------------------------------- state

    private void Publish()
    {
        if(!window.Ready)return;
        var profile=settings.Account;
        var account=profile is not null&&phase is "ready" or "offline"
            ?new UiAccount(profile.Name,profile.Username,Initial(profile.Name),profile.Avatar,profile.Email,profile.EmailVerified,
                profile.TotpEnabled,LocalDate(profile.Joined),LocalTime(profile.LastLogin)):null;
        var state=new UiState("state",Developer?"dev":"user",settings.Theme,phase,MomokoOrigin,account,
            new UiLogin(loginBusy,authorization?.UserCode,loginError),apps.Select(ServerState).ToArray(),settings.Selected,notice,
            UpdateState(),new UiWindow(settings.CloseAction),DeveloperState());
        window.Post(JsonSerializer.Serialize(state,LauncherJson.Default.UiState));
    }

    // The game name Momoko reports for the server, the module id when unknown.
    private string TitleOf(MomokoLaunchApp server)=>string.IsNullOrWhiteSpace(server.Server.GameName)?ModuleOf(server.Server.Game)!.Id:server.Server.GameName!;

    private static string Initial(string name)=>string.IsNullOrEmpty(name)?"?":char.ToUpperInvariant(name[0]).ToString();

    // Momoko reports unset times as missing or as the Unix epoch.
    private static string LocalDate(DateTimeOffset? value)=>value is {} time&&time.Year>1970
        ?time.ToLocalTime().ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):"";

    private static string LocalTime(DateTimeOffset? value)=>value is {} time&&time.Year>1970
        ?time.ToLocalTime().ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture):"";

    private UiServer ServerState(MomokoLaunchApp server)
    {
        var module=ModuleOf(server.Server.Game)!;
        var game=settings.Game(module.Id);
        var executable=game.Executable??module.DefaultExecutable;
        var valid=true;
        try{module.ValidateExecutable(executable);}catch(Exception){valid=false;}
        var run=RunOf(module.Id);
        var status=run.Status=="idle"||run.AppId is null||run.AppId==server.AppId?run.Status:"busy";
        UiLocalization? localization=null;
        if(module.SupportsLocalization)
        {
            var package=module.Localization;
            localization=new UiLocalization(game.Localization&&package is not null,package is null?null:
                new UiPackage(package.Source,package.Version,package.Files,package.ImportedAt.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        }
        var link=server.Link;
        var background=MediaCache.Local(server.Background);
        return new UiServer(server.AppId,server.Server.Name,module.Id,TitleOf(server),
            MediaCache.Local(server.Icon)??"",background??"",MediaCache.IsVideo(background),server.Server.State,server.Server.Online,
            link is null?null:new UiPlayer(link.PlayerId,link.DisplayId??"",link.Nickname??"",link.Account??"",link.Level,
                link.Method??"",LocalDate(link.LinkedAt),LocalTime(link.LastSeenAt)),
            server.Bindable,server.BlockReason??"",status,new UiExecutable(executable,valid,module.ExecutableName),localization,
            module.SupportsFreeResolution?game.FreeResolution:null,
            DeveloperEndpointsState(server),DeveloperServerState(server));
    }
}
