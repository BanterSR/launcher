using System.Text;

namespace Momoko.Launcher.Core;

internal enum LogLevel{Info,Ok,Warn,Error}

// log/launcher.log keeps every line unredacted. The developer build also
// streams the lines to its log panel through Written.
internal static class LauncherLog
{
    private static readonly object Gate=new();
    public static event Action<DateTime,LogLevel,string>? Written;

    public static void Write(string text,LogLevel level=LogLevel.Info)
    {
        var now=DateTime.Now;
        try
        {
            lock(Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs,"launcher.log"),
                    $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}",Encoding.UTF8);
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
        Written?.Invoke(now,level,text);
    }

    public static void Ok(string text)=>Write(text,LogLevel.Ok);
    public static void Warn(string text)=>Write(text,LogLevel.Warn);
    public static void Error(string text)=>Write(text,LogLevel.Error);
}
