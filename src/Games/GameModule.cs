namespace Momoko.Launcher.Games;

// Client connection origins of one game server. Momoko publishes them per
// server; the developer build may override them.
internal sealed record GameEndpoints(string Sdk,string Game,string Resource);

// The game server SDK session a launch hands to the native hook.
internal sealed record SdkSession(string IdToken,string PlatformToken,string RefreshToken,string UserId,string PlayerId,string PublicUserId);

internal sealed record GameLaunch(string ExecutablePath,GameEndpoints Endpoints,SdkSession Session,bool Localization,
    string LocalizationFont,bool FreeResolution,bool ClientConsole,IReadOnlyList<string> Arguments);

internal sealed record LocalizationInfo(string Source,string Version,DateTime ImportedAt,int Files,int Ignored,string[] Skipped);

// A game the launcher can start. Each module is compiled in; its id matches the
// Momoko game adapter id of the servers it can launch. How the game is shown
// (name, wordmark, icon, backgrounds) is a Momoko launcher setting.
internal abstract class GameModule
{
    public abstract string Id { get; }
    public abstract string ExecutableName { get; }
    public abstract string DefaultExecutable { get; }
    public virtual bool SupportsLocalization=>false;
    // The launcher can let the player resize the game window freely.
    public virtual bool SupportsFreeResolution=>false;

    public string DataFolder=>Core.AppPaths.GameData(Id);

    public abstract void ValidateExecutable(string path);
    public abstract Task<int> LaunchAsync(GameLaunch launch,IProgress<string> progress,CancellationToken cancellation);
    // Game path of a session whose cleanup has not finished yet.
    public abstract string? PendingSession();
    public abstract bool IsRunning(string executablePath);
    public abstract IReadOnlyList<string> Cleanup();

    public virtual LocalizationInfo? Localization=>null;
    public virtual LocalizationInfo ImportLocalization(string zipPath,IProgress<string> progress,CancellationToken cancellation)
        =>throw new NotSupportedException(Id+" 没有汉化功能。");
}
