using Microsoft.Win32;

namespace Momoko.Launcher.Host;

internal static unsafe class Dialogs
{
    // filter: pairs of "描述","*.ext;*.ext2".
    public static string? OpenFile(nint owner,string title,string initialPath,params string[] filter)
    {
        var filterText=string.Join('\0',filter)+"\0\0";
        var buffer=new char[32768];
        if(!string.IsNullOrEmpty(initialPath)&&initialPath.Length<buffer.Length-1)initialPath.CopyTo(0,buffer,0,initialPath.Length);
        var directory=string.IsNullOrEmpty(initialPath)?null:Path.GetDirectoryName(initialPath);
        fixed(char* file=buffer)
        fixed(char* filters=filterText)
        fixed(char* caption=title)
        fixed(char* folder=directory)
        {
            var options=new Win32.OPENFILENAMEW
            {
                StructSize=(uint)sizeof(Win32.OPENFILENAMEW),
                Owner=owner,
                Filter=filters,
                FilterIndex=1,
                File=file,
                MaxFile=(uint)buffer.Length,
                InitialDir=directory is not null&&Directory.Exists(directory)?folder:null,
                Title=caption,
                Flags=Win32.OFN_EXPLORER|Win32.OFN_FILEMUSTEXIST|Win32.OFN_PATHMUSTEXIST|Win32.OFN_NOCHANGEDIR
            };
            if(!Win32.GetOpenFileNameW(ref options))return null;
        }
        var end=Array.IndexOf(buffer,'\0');
        return new string(buffer,0,end<0?buffer.Length:end);
    }

    public static void Error(nint owner,string text)=>Win32.MessageBoxW(owner,text,"Momoko 启动器",Win32.MB_OK|Win32.MB_ICONERROR);

    // Windows "app mode" (Settings > Personalization > Colors).
    public static bool SystemDark()
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light&&light==0;
    }
}
