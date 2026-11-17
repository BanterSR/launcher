namespace Momoko.Launcher.App;

// Messages the host posts to ui/app.js. One "state" message carries the whole
// view model; the UI renders from it and sends actions back.
internal sealed record UiState(
    string Type,
    string Edition,
    string Theme,
    string Phase,
    string Momoko,
    UiAccount? Account,
    UiLogin Login,
    UiServer[] Servers,
    string? Selected,
    string? Notice,
    string Announcement,
    UiUpdate Update,
    UiDeveloper? Developer);

// Version: this build's version. Enabled: this build checks GitHub releases.
// Latest/Page: a newer release and its page, null when there is none.
internal sealed record UiUpdate(string Version,bool Enabled,bool Checking,string? Latest,string? Page,string? Error);

internal sealed record UiAccount(string Name,string Username,string Initial,string Avatar);

// Code: the user code of a browser authorization in progress.
internal sealed record UiLogin(bool Busy,string? Code,string? Error);

// Icon is the server's image in Momoko (square version of its cover). Title,
// Wordmark and Art come from the Momoko launcher settings of the game; without
// settings Title is the game id and Wordmark and Art are empty.
internal sealed record UiServer(
    string Id,
    string Name,
    string Game,
    string Title,
    string Wordmark,
    string Icon,
    string[] Art,
    string? State,
    int Online,
    UiPlayer? Player,
    bool Bindable,
    string BlockReason,
    string Status,
    UiExecutable Executable,
    UiLocalization? Localization,
    UiEndpoints? Endpoints,
    UiDeveloperServer? Developer);

internal sealed record UiPlayer(string Id,string DisplayId,string Nickname);

internal sealed record UiExecutable(string Path,bool Valid,string Name);

internal sealed record UiLocalization(bool Enabled,UiPackage? Package);

internal sealed record UiPackage(string Name,string Version,int Files,string ImportedAt);

internal sealed record UiEndpoints(string Sdk,string Game,string Resource,string MomokoSdk,string MomokoGame,string MomokoResource);

internal sealed record UiDeveloperServer(string LoginMethod,string Account,bool PasswordSaved);

internal sealed record UiDeveloper(string Momoko,string ConfigMomoko,bool ClientConsole,string Font,string Arguments,string Status,string Pid);

internal sealed record UiLog(string Type,string Time,string Level,string Text);

internal sealed record UiToast(string Type,string Kind,string Text);
