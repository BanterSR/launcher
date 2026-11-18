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
    UiUpdate Update,
    UiWindow Window,
    UiDeveloper? Developer);

// Version: this build's version. Enabled: this build checks GitHub releases.
// Latest/Page/Notes: a newer release, its page and release notes, null when
// there is none. Installable: the release has a launcher executable with a
// SHA-256 digest. Installing/Progress: a one-click update in progress and its
// download percent (-1 when none). Blocked: why updating must wait.
internal sealed record UiUpdate(string Version,bool Enabled,bool Checking,string? Latest,string? Page,string? Notes,
    bool Installable,bool Installing,int Progress,string? Blocked,string? Error);

// Window behavior: CloseAction is "ask", "tray" or "exit".
internal sealed record UiWindow(string CloseAction);

// A message without data: "update.prompt" opens the update dialog,
// "close.ask" asks what closing the window should do.
internal sealed record UiSignal(string Type);

// Passport card: Joined is a local date (yyyy-MM-dd), LastLogin a local time
// (yyyy-MM-dd HH:mm); empty when Momoko reports none.
internal sealed record UiAccount(string Name,string Username,string Initial,string Avatar,string Email,bool EmailVerified,
    bool TotpEnabled,string Joined,string LastLogin);

// Code: the user code of a browser authorization in progress.
internal sealed record UiLogin(bool Busy,string? Code,string? Error);

// FreeResolution: the game's free window resizing switch, null when the game
// has none.
// Title is the game name Momoko reports for the server (game id when unknown).
// Icon and Background are the cached files of the links Momoko hands out
// (https://media.momoko.example/...), empty until downloaded or when not set.
internal sealed record UiServer(
    string Id,
    string Name,
    string Game,
    string Title,
    string Icon,
    string Background,
    bool BackgroundVideo,
    string? State,
    int Online,
    UiPlayer? Player,
    bool Bindable,
    string BlockReason,
    string Status,
    UiExecutable Executable,
    UiLocalization? Localization,
    bool? FreeResolution,
    UiEndpoints? Endpoints,
    UiDeveloperServer? Developer);

// The passport's game account on a server (Momoko link). Method is the
// binding method id (password, code, create, admin, request); LinkedAt a
// local date, LastSeen a local time, empty when unknown.
internal sealed record UiPlayer(string Id,string DisplayId,string Nickname,string Account,int Level,string Method,string LinkedAt,string LastSeen);

internal sealed record UiExecutable(string Path,bool Valid,string Name);

internal sealed record UiLocalization(bool Enabled,UiPackage? Package);

internal sealed record UiPackage(string Name,string Version,int Files,string ImportedAt);

internal sealed record UiEndpoints(string Sdk,string Game,string Resource,string MomokoSdk,string MomokoGame,string MomokoResource);

internal sealed record UiDeveloperServer(string LoginMethod,string Account,bool PasswordSaved);

internal sealed record UiDeveloper(string Momoko,string ConfigMomoko,bool ClientConsole,string Font,string Arguments,string Status,string Pid);

internal sealed record UiLog(string Type,string Time,string Level,string Text);

internal sealed record UiToast(string Type,string Kind,string Text);
