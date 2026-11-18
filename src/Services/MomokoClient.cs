using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Momoko.Launcher.Core;

namespace Momoko.Launcher.Services;

// Momoko passport first-party API (docs/login-protocol.md sections 1-4).
// Replies use {code,message,data}; failures carry a PASSPORT_* code in message.
internal sealed class MomokoClient:IDisposable
{
    private readonly HttpClient http=new(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(20)};
    public string Origin { get; private set; }

    public MomokoClient(string origin){Origin=origin;}

    public void SetOrigin(string origin){Origin=origin;}

    // Browser authorization (docs/login-protocol.md section 1.1).
    public Task<MomokoClientAuthorization> AuthorizeClientAsync(MomokoClientAuthRequest request,CancellationToken cancellation)
        =>SendAsync("POST","/api/v1/auth/client",null,request,LauncherJson.Default.MomokoClientAuthRequest,
            LauncherJson.Default.MomokoEnvelopeMomokoClientAuthorization,cancellation);

    public Task<MomokoTokens> ClientTokenAsync(string deviceCode,CancellationToken cancellation)
        =>SendAsync("POST","/api/v1/auth/client/token",null,new MomokoClientTokenRequest(deviceCode),LauncherJson.Default.MomokoClientTokenRequest,
            LauncherJson.Default.MomokoEnvelopeMomokoTokens,cancellation);

    public Task<MomokoTokens> RefreshAsync(string refreshToken,CancellationToken cancellation)
        =>SendAsync("POST","/api/v1/auth/refresh",null,new MomokoRefreshRequest(refreshToken),LauncherJson.Default.MomokoRefreshRequest,
            LauncherJson.Default.MomokoEnvelopeMomokoTokens,cancellation);

    public async Task LogoutAsync(string accessToken,CancellationToken cancellation)
        =>await SendAsync("POST","/api/v1/auth/logout",accessToken,new MomokoEmpty(),LauncherJson.Default.MomokoEmpty,
            LauncherJson.Default.MomokoEnvelopeJsonElement,cancellation);

    public Task<MomokoMe> MeAsync(string accessToken,CancellationToken cancellation)
        =>SendAsync<MomokoEmpty,MomokoMe>("GET","/api/v1/me",accessToken,null,null,LauncherJson.Default.MomokoEnvelopeMomokoMe,cancellation);

    // Launch targets are addressed by client app ID (docs/login-protocol.md sections 2-5).
    public Task<MomokoLaunchApps> AppsAsync(string accessToken,CancellationToken cancellation)
        =>SendAsync<MomokoEmpty,MomokoLaunchApps>("GET","/api/v1/launch/apps",accessToken,null,null,
            LauncherJson.Default.MomokoEnvelopeMomokoLaunchApps,cancellation);

    public Task<MomokoLink> BindAsync(string accessToken,string appId,MomokoLaunchBindRequest request,CancellationToken cancellation)
        =>SendAsync("POST",AppPath(appId,"/bind"),accessToken,request,LauncherJson.Default.MomokoLaunchBindRequest,
            LauncherJson.Default.MomokoEnvelopeMomokoLink,cancellation);

    public Task<MomokoLink> CreateAccountAsync(string accessToken,string appId,CancellationToken cancellation)
        =>SendAsync("POST",AppPath(appId,"/account"),accessToken,new MomokoEmpty(),
            LauncherJson.Default.MomokoEmpty,LauncherJson.Default.MomokoEnvelopeMomokoLink,cancellation);

    public Task<MomokoTicket> TicketAsync(string accessToken,string appId,CancellationToken cancellation)
        =>SendAsync("POST",AppPath(appId,"/ticket"),accessToken,new MomokoEmpty(),
            LauncherJson.Default.MomokoEmpty,LauncherJson.Default.MomokoEnvelopeMomokoTicket,cancellation);

    private static string AppPath(string appId,string action)=>"/api/v1/launch/apps/"+Uri.EscapeDataString(appId)+action;

    public string PageUrl(string path)=>Origin.TrimEnd('/')+path;

    private async Task<TResult> SendAsync<TBody,TResult>(string method,string path,string? accessToken,TBody? body,
        JsonTypeInfo<TBody>? bodyType,JsonTypeInfo<MomokoEnvelope<TResult>> resultType,CancellationToken cancellation)
        where TResult:notnull
    {
        using var request=new HttpRequestMessage(new HttpMethod(method),Origin.TrimEnd('/')+path);
        if(accessToken is not null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",accessToken);
        if(body is not null&&bodyType is not null)
            request.Content=new StringContent(JsonSerializer.Serialize(body,bodyType),Encoding.UTF8,"application/json");
        HttpResponseMessage response;
        try{response=await http.SendAsync(request,cancellation);}
        catch(HttpRequestException error){throw new MomokoUnavailableException("无法连接 Momoko 通行证："+error.Message,error);}
        catch(TaskCanceledException error) when(!cancellation.IsCancellationRequested)
        {throw new MomokoUnavailableException("连接 Momoko 通行证超时。",error);}
        using(response)
        {
            var text=await response.Content.ReadAsStringAsync(cancellation);
            if(!response.IsSuccessStatusCode)throw MomokoException.From(response.StatusCode,text);
            MomokoEnvelope<TResult>? envelope;
            try{envelope=JsonSerializer.Deserialize(text,resultType);}
            catch(JsonException error){throw new MomokoUnavailableException("Momoko 通行证返回了无法识别的内容。",error);}
            var data=envelope is null?default:envelope.Data;
            if(data is null)throw new MomokoUnavailableException("Momoko 通行证返回了空数据。");
            return data;
        }
    }

    public void Dispose()=>http.Dispose();
}

internal class MomokoException(HttpStatusCode status,string code,string message):Exception(message)
{
    public HttpStatusCode Status { get; }=status;
    public string Code { get; }=code;

    public bool Unauthorized=>Status==HttpStatusCode.Unauthorized||Code=="PASSPORT_UNAUTHORIZED";

    public static MomokoException From(HttpStatusCode status,string body)
    {
        var code="";
        try
        {
            var error=JsonSerializer.Deserialize(body,LauncherJson.Default.MomokoError);
            code=error?.Message??"";
        }
        catch(JsonException){}
        return new MomokoException(status,code,Describe(status,code));
    }

    public static string Describe(HttpStatusCode status,string code)=>code switch
    {
        "PASSPORT_AUTH_PENDING"=>"还没有在浏览器中授权。",
        "PASSPORT_AUTH_SLOW_DOWN"=>"授权查询过于频繁。",
        "PASSPORT_AUTH_DENIED"=>"已在浏览器中拒绝授权。",
        "PASSPORT_AUTH_EXPIRED"=>"授权已过期，请重新登录。",
        "PASSPORT_TOO_MANY_REQUESTS"=>"请求过于频繁，请稍后再试。",
        "PASSPORT_PUBLIC_URL_UNSET"=>"Momoko 通行证还没有配置对外地址，请联系服务器维护者。",
        "PASSPORT_ACCOUNT_DISABLED"=>"这个通行证账号已被停用。",
        "PASSPORT_UNAUTHORIZED"=>"登录已失效，请重新登录。",
        "PASSPORT_INVALID_ARGUMENT"=>"请求参数无效。",
        "PASSPORT_GAME_ACCOUNT_REQUIRED"=>"还没有绑定这台服务器的游戏账号。",
        "PASSPORT_SERVER_NOT_FOUND"=>"这台游戏服务器已不存在。",
        "PASSPORT_APP_NOT_FOUND"=>"这个启动目标已不存在，请刷新服务器列表。",
        "PASSPORT_SERVER_NOT_BINDABLE"=>"这台服务器暂不开放绑定。",
        "PASSPORT_BIND_UNSUPPORTED"=>"这台服务器不支持绑定游戏账号。",
        "PASSPORT_ALREADY_LINKED"=>"这台服务器上已经绑定了游戏账号。",
        "PASSPORT_GAME_ACCOUNT_TAKEN"=>"这个游戏账号已被其他通行证绑定。",
        "PASSPORT_BIND_COOLDOWN"=>"刚解绑过这台服务器的账号，冷却期内只能重新绑定原账号。",
        "PASSPORT_GAME_CREDENTIALS_INVALID"=>"游戏账号或密码不正确。",
        "PASSPORT_BIND_TOO_MANY_ATTEMPTS"=>"绑定尝试次数过多，请稍后再试。",
        "PASSPORT_GAME_UNAVAILABLE"=>"游戏服务器暂时无法连接。",
        "PASSPORT_GAME_PLAYER_NOT_FOUND"=>"游戏账号不存在。",
        ""=>$"Momoko 通行证返回错误（HTTP {(int)status}）。",
        _=>$"Momoko 通行证返回错误：{code}"
    };
}

internal sealed class MomokoUnavailableException(string message,Exception? inner=null):Exception(message,inner);

internal sealed record MomokoEnvelope<T>(int Code,string? Message,T? Data);
internal sealed record MomokoError(int Code,string? Message);
internal sealed record MomokoEmpty;
internal sealed record MomokoClientAuthRequest(string Client,string DeviceId,string Device);
internal sealed record MomokoClientAuthorization(string DeviceCode,string UserCode,string VerificationUrl,long Interval,long ExpiresIn);
internal sealed record MomokoClientTokenRequest(string DeviceCode);
internal sealed record MomokoRefreshRequest(string RefreshToken);
internal sealed record MomokoTokens(string? AccessToken,string? RefreshToken,long ExpiresIn);
internal sealed record MomokoMe(string Id,string Username,string? Name,string? Email,bool EmailVerified,string? Avatar,
    bool TotpEnabled,DateTimeOffset? CreateTime,DateTimeOffset? LastLoginAt);
internal sealed record MomokoServerBrief(string Id,string Game,string? GameName,string Name,string? State,int Online,string? Theme,string? Cover);
internal sealed record MomokoLink(string Id,string PlayerId,string? DisplayId,string? Account,string? Nickname,int Level,string? Method,
    DateTimeOffset? LinkedAt,DateTimeOffset? LastSeenAt);
internal sealed record MomokoEndpoints(string Sdk,string Game,string Resource);
// Icon and Background: links (object storage) of the game icon and the game
// page background (image or video) uploaded for this server; empty when not set.
internal sealed record MomokoLaunchApp(string AppId,MomokoServerBrief Server,MomokoEndpoints Endpoints,MomokoLink? Link,bool Bindable,string? BlockReason,
    string? Icon,string? Background);
internal sealed record MomokoLaunchApps(MomokoLaunchApp[] Apps);
internal sealed record MomokoLaunchBindRequest(string Account,string Password);
internal sealed record MomokoTicket(string Ticket,long ExpiresIn,string PlayerId,string? DisplayId);
