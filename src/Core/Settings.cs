using System.Text.Json;

namespace Momoko.Launcher.Core;

// data\settings.json: per-user choices. Secrets stay in Credential Manager.
internal sealed class LauncherSettings
{
    public string Theme { get; set; } = "system";
    // Closing the window: "ask" (the close dialog), "tray" (hide to the
    // notification area) or "exit".
    public string CloseAction { get; set; } = "ask";
    public string DeviceId { get; set; } = "";
    public string? Selected { get; set; }
    public AccountProfile? Account { get; set; }
    public Dictionary<string,GameSettings> Games { get; set; } = new();
    public DeveloperSettings Developer { get; set; } = new();

    public static LauncherSettings Load()
    {
        LauncherSettings? settings=null;
        if(File.Exists(AppPaths.Settings))
        {
            try{settings=JsonSerializer.Deserialize(File.ReadAllText(AppPaths.Settings),LauncherJson.Default.LauncherSettings);}
            catch(JsonException error){LauncherLog.Warn("settings.json 无法读取，已重新创建："+error.Message);}
        }
        settings??=new LauncherSettings();
        if(settings.Theme is not ("system" or "light" or "dark"))settings.Theme="system";
        if(settings.CloseAction is not ("ask" or "tray" or "exit"))settings.CloseAction="ask";
        if(string.IsNullOrEmpty(settings.DeviceId))settings.DeviceId=Guid.NewGuid().ToString("N");
        settings.Games??=new();
        settings.Developer??=new();
        settings.Developer.Servers??=new();
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Data);
        var temporary=AppPaths.Settings+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(this,LauncherJson.Default.LauncherSettings));
        File.Move(temporary,AppPaths.Settings,true);
    }

    public GameSettings Game(string id)
    {
        if(!Games.TryGetValue(id,out var game))Games[id]=game=new GameSettings();
        return game;
    }
}

internal sealed class GameSettings
{
    public string? Executable { get; set; }
    public bool Localization { get; set; }
    // Games whose module supports it: the game window can be resized freely
    // (gakumas: within the client's portrait/landscape aspect range).
    public bool FreeResolution { get; set; } = true;
}

// Shown on the account chip and the passport card while the passport is
// signed in (also offline, from this cache).
internal sealed class AccountProfile
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Email { get; set; } = "";
    public bool EmailVerified { get; set; }
    public bool TotpEnabled { get; set; }
    public DateTimeOffset? Joined { get; set; }
    public DateTimeOffset? LastLogin { get; set; }
}

// Only the developer build reads or edits these.
internal sealed class DeveloperSettings
{
    public string? Momoko { get; set; }
    public bool ClientConsole { get; set; } = true;
    public string Font { get; set; } = @"C:\Windows\Fonts\msyh.ttc";
    public string Arguments { get; set; } = "";
    public Dictionary<string,DeveloperServer> Servers { get; set; } = new();
}

internal sealed class DeveloperServer
{
    public string? Sdk { get; set; }
    public string? Game { get; set; }
    public string? Resource { get; set; }
    public string LoginMethod { get; set; } = "passport";
    public string Account { get; set; } = "";
}

internal static class Origins
{
    public static string Normalize(string? value,string field)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Contains('\0')||
            !Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||
            string.IsNullOrEmpty(uri.Host)||uri.AbsolutePath!="/"||!string.IsNullOrEmpty(uri.UserInfo)||
            !string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException(field+" 必须是 HTTP(S) 域名或 IP，可带端口，不含路径。");
        var origin=uri.GetLeftPart(UriPartial.Authority);
        if(origin.Length>=256)throw new InvalidDataException(field+" 过长，最多 255 个字符。");
        return origin;
    }
}
