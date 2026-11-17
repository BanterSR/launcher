namespace Momoko.Launcher.Core;

// The launcher ships as one executable; everything it writes lives next to it,
// so the folder can be moved or copied as a whole:
//   data\settings.json, data\webview2\, data\media\ (cached game icons and
//   backgrounds), data\games\<game>\ (session marker, imported localization)
//   and log\.
internal static class AppPaths
{
    public static readonly string Base=AppContext.BaseDirectory;
    public static readonly string Data=Path.Combine(Base,"data");
    public static readonly string WebView=Path.Combine(Data,"webview2");
    public static readonly string Media=Path.Combine(Data,"media");
    public static readonly string Settings=Path.Combine(Data,"settings.json");
    public static readonly string Logs=Path.Combine(Base,"log");

    public static string GameData(string id)=>Path.Combine(Data,"games",id);
}
