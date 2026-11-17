using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Momoko.Launcher.Core;
using Momoko.Launcher.Games;

namespace Momoko.Launcher.Services;

// Game server SDK: exchanges a Momoko launch ticket for an SDK session
// (POST /passport/ticket-login). The developer build can also log in with a
// game account and password (POST /account/login) to test a server directly.
internal sealed class SdkClient:IDisposable
{
    private readonly HttpClient http=new(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(20)};

    public async Task<SdkSession> TicketLoginAsync(string sdkOrigin,string ticket,CancellationToken cancellation)
    {
        var reply=await PostAsync(sdkOrigin,"/passport/ticket-login",new SdkTicketRequest(ticket),
            LauncherJson.Default.SdkTicketRequest,LauncherJson.Default.SdkSessionReply,cancellation);
        return Validate(reply,true);
    }

#if DEV
    // Developer build: game account password login for test servers.
    public async Task<SdkSession> PasswordLoginAsync(string sdkOrigin,string account,string password,CancellationToken cancellation)
    {
        var reply=await PostAsync(sdkOrigin,"/account/login",new SdkPasswordRequest(account,password),
            LauncherJson.Default.SdkPasswordRequest,LauncherJson.Default.SdkSessionReply,cancellation);
        return Validate(reply,false);
    }
#endif

    private static SdkSession Validate(SdkSessionReply? reply,bool ticket)
    {
        if(reply is null||string.IsNullOrEmpty(reply.IdToken)||string.IsNullOrEmpty(reply.RefreshToken)||reply.RefreshToken.Length>=4096||
            string.IsNullOrEmpty(reply.PlatformToken)||reply.PlatformToken.Length>=4096||
            string.IsNullOrEmpty(reply.UserId)||reply.UserId.Length>=128||(ticket&&string.IsNullOrEmpty(reply.PlayerId)))
            throw new InvalidDataException("游戏服务器返回的会话字段缺失或过长。");
        return new SdkSession(reply.IdToken,reply.PlatformToken,reply.RefreshToken,reply.UserId,reply.PlayerId??"",reply.PublicUserId??"");
    }

    private async Task<TReply?> PostAsync<TBody,TReply>(string origin,string path,TBody body,JsonTypeInfo<TBody> bodyType,
        JsonTypeInfo<TReply> replyType,CancellationToken cancellation)
    {
        using var content=new StringContent(JsonSerializer.Serialize(body,bodyType),Encoding.UTF8,"application/json");
        HttpResponseMessage response;
        try{response=await http.PostAsync(origin.TrimEnd('/')+path,content,cancellation);}
        catch(HttpRequestException error){throw new SdkException(0,"","无法连接游戏服务器："+error.Message);}
        catch(TaskCanceledException) when(!cancellation.IsCancellationRequested){throw new SdkException(0,"","连接游戏服务器超时。");}
        using(response)
        {
            var text=await response.Content.ReadAsStringAsync(cancellation);
            if(!response.IsSuccessStatusCode)throw SdkException.From(response.StatusCode,text);
            try{return JsonSerializer.Deserialize(text,replyType);}
            catch(JsonException){throw new InvalidDataException("游戏服务器返回了无法识别的内容。");}
        }
    }

    public void Dispose()=>http.Dispose();
}

internal sealed class SdkException(int status,string code,string message):Exception(message)
{
    public int Status { get; }=status;
    public string Code { get; }=code;

    public static SdkException From(HttpStatusCode status,string body)
    {
        var code="";
        try{code=JsonSerializer.Deserialize(body,LauncherJson.Default.SdkErrorReply)?.Error?.Message??"";}
        catch(JsonException){}
        var message=code switch
        {
            "INVALID_TICKET"=>"启动票据无效或已过期，请重试。",
            "PLAYER_NOT_FOUND"=>"游戏服务器上找不到这个游戏账号。",
            "account banned"=>"这个游戏账号处于封禁期。",
            "TICKET_LOGIN_DISABLED"=>"游戏服务器没有开启通行证登录。",
            "invalid account or password"=>"游戏账号或密码不正确。",
            ""=>$"游戏服务器返回错误（HTTP {(int)status}）。",
            _=>"游戏服务器返回错误："+code
        };
        return new SdkException((int)status,code,message);
    }
}

internal sealed record SdkTicketRequest(string Ticket);
#if DEV
internal sealed record SdkPasswordRequest(string Account,string Password);
#endif
internal sealed record SdkSessionReply(string? IdToken,string? PlatformToken,string? RefreshToken,string? UserId,string? PlayerId,string? PublicUserId);
internal sealed record SdkErrorReply(SdkErrorBody? Error);
internal sealed record SdkErrorBody(string? Message);
