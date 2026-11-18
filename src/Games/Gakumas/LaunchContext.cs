using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Momoko.Launcher.Core;

namespace Momoko.Launcher.Games.Gakumas;

// The launch context reaches the native hook through the game process
// environment: GAKUMAS_LAUNCH_CONTEXT holds the version, every field uses the
// GAKUMAS_LAUNCH_CONTEXT_ prefix. The native side checks length and presence.
internal sealed class LaunchContext
{
    public const string Version="10";
    private const string EnvironmentVariable="GAKUMAS_LAUNCH_CONTEXT";
    private readonly Dictionary<string,string> environment=new(){{EnvironmentVariable,Version}};
    public string PlayerLogPath { get; }
    public string PlatformId { get; }

    public LaunchContext(GameLaunch launch)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        PlayerLogPath=Path.Combine(AppPaths.Logs,$"client-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        PlatformId=PlatformIdOf(launch.Session.UserId);
        Put("SDK_ORIGIN",256,launch.Endpoints.Sdk);
        Put("GAME_ORIGIN",256,launch.Endpoints.Game);
        // No resource server address: the game keeps the official asset hosts.
        if(!string.IsNullOrEmpty(launch.Endpoints.Resource))Put("RESOURCE_ORIGIN",256,launch.Endpoints.Resource);
        Put("REFRESH_TOKEN",4096,launch.Session.RefreshToken);
        Put("PLATFORM_TOKEN",4096,launch.Session.PlatformToken);
        Put("USER_ID",128,launch.Session.UserId);
        Put("PLATFORM_ID",128,PlatformId);
        Put("SHOW_CONSOLE",2,launch.ClientConsole?"1":"0");
        Put("LOG_DIRECTORY",1024,AppPaths.Logs);
        Put("PLAYER_LOG_PATH",1024,PlayerLogPath);
        Put("LOCALIZATION",2,launch.Localization?"1":"0");
        if(launch.Localization)
        {
            Put("LOCALIZATION_DIRECTORY",1024,LocalizationPackage.DirectoryPath);
            Put("LOCALIZATION_FONT",1024,launch.LocalizationFont);
        }
        Put("FREE_RESOLUTION",2,launch.FreeResolution?"1":"0");
    }

    // The DMM viewer id the client expects: a stable 9-digit number from the
    // SDK user id (the local Recibo /v2/pc/sdk/initialize uses the same rule).
    public static string PlatformIdOf(string userId)
    {
        var digest=SHA256.HashData(Encoding.UTF8.GetBytes(userId));
        return (BitConverter.ToUInt32(digest)%900_000_000+100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Put(string field,int capacity,string value)
    {
        if(string.IsNullOrEmpty(value)||value.Length>=capacity||value.Contains('\0'))
            throw new InvalidDataException("启动上下文字段无效："+field);
        environment.Add(EnvironmentVariable+"_"+field,value);
    }

    public void ApplyTo(ProcessStartInfo start)
    {
        foreach(var (name,value) in environment)start.Environment[name]=value;
    }
}
