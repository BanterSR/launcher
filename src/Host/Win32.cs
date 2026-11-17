using System.Runtime.InteropServices;

namespace Momoko.Launcher.Host;

// Win32 surface used by the host window. LibraryImport keeps every call
// source-generated, so the user build can be compiled with NativeAOT.
internal static unsafe partial class Win32
{
    public const uint WS_POPUP=0x80000000,WS_CAPTION=0x00C00000,WS_SYSMENU=0x00080000,WS_THICKFRAME=0x00040000,
        WS_MINIMIZEBOX=0x00020000,WS_CLIPCHILDREN=0x02000000;
    public const uint WS_EX_APPWINDOW=0x00040000;
    public const int SW_SHOWNORMAL=1,SW_MINIMIZE=6,SW_RESTORE=9,SW_SHOW=5;
    public const uint WM_DESTROY=0x0002,WM_MOVE=0x0003,WM_SIZE=0x0005,WM_ACTIVATE=0x0006,WM_CLOSE=0x0010,
        WM_SETTINGCHANGE=0x001A,WM_NCCALCSIZE=0x0083,WM_NCHITTEST=0x0084,WM_NCLBUTTONDOWN=0x00A1,
        WM_DPICHANGED=0x02E0,WM_APP=0x8000;
    public const int HTCLIENT=1,HTCAPTION=2;
    public const nint SIZE_MINIMIZED=1,WA_INACTIVE=0;
    public const uint SWP_NOZORDER=0x0004,SWP_NOACTIVATE=0x0010,SWP_FRAMECHANGED=0x0020;
    public const uint MONITOR_DEFAULTTOPRIMARY=1;
    public const uint MB_OK=0x0,MB_ICONERROR=0x10,MB_ICONWARNING=0x30,MB_OKCANCEL=0x1,MB_YESNO=0x4,IDOK=1,IDYES=6;
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE=20,DWMWA_WINDOW_CORNER_PREFERENCE=33,DWMWA_BORDER_COLOR=34;
    public const int DWMWCP_ROUND=2;
    public const uint DWMWA_COLOR_NONE=0xFFFFFFFE;
    public const uint COINIT_APARTMENTTHREADED=0x2;
    public const int IDC_ARROW=32512,IDI_APPLICATION=32512;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left,Top,Right,Bottom;
        public readonly int Width=>Right-Left;
        public readonly int Height=>Bottom-Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X,Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint Window;
        public uint Message;
        public nint WParam,LParam;
        public uint Time;
        public POINT Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint Size,Style;
        public delegate* unmanaged<nint,uint,nint,nint,nint> WindowProc;
        public int ClassExtra,WindowExtra;
        public nint Instance,Icon,Cursor,Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left,Right,Top,Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint Size;
        public RECT Monitor,Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OPENFILENAMEW
    {
        public uint StructSize;
        public nint Owner,Instance;
        public char* Filter;
        public char* CustomFilter;
        public uint MaxCustomFilter,FilterIndex;
        public char* File;
        public uint MaxFile;
        public char* FileTitle;
        public uint MaxFileTitle;
        public char* InitialDir;
        public char* Title;
        public uint Flags;
        public ushort FileOffset,FileExtension;
        public char* DefaultExtension;
        public nint CustomData,Hook;
        public char* TemplateName;
        public nint Reserved1;
        public uint Reserved2,FlagsEx;
    }

    public const uint OFN_FILEMUSTEXIST=0x00001000,OFN_PATHMUSTEXIST=0x00000800,OFN_NOCHANGEDIR=0x00000008,OFN_EXPLORER=0x00080000;

    [LibraryImport("user32.dll",SetLastError=true)]
    public static partial ushort RegisterClassExW(in WNDCLASSEXW windowClass);

    [LibraryImport("user32.dll",SetLastError=true,StringMarshalling=StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle,string className,string windowName,uint style,
        int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint window,uint message,nint wParam,nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG message,nint window,uint min,uint max);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG message);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(in MSG message);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint window,uint message,nint wParam,nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window,int command);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window,out RECT rect);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint window,nint insertAfter,int x,int y,int width,int height,uint flags);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint window,uint message,nint wParam,nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint instance,nint name);

    [LibraryImport("user32.dll")]
    public static partial nint LoadIconW(nint instance,nint name);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT point,uint flags);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor,ref MONITORINFO info);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlashWindow(nint window,[MarshalAs(UnmanagedType.Bool)] bool invert);

    // Returns an IStream* over a copy of the bytes (shlwapi).
    [LibraryImport("shlwapi.dll")]
    public static partial nint SHCreateMemStream(byte* data,uint size);

    [LibraryImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    public static partial nint GetActiveWindow();

    [LibraryImport("user32.dll",StringMarshalling=StringMarshalling.Utf16)]
    public static partial nint FindWindowW(string className,string? windowName);

    [LibraryImport("user32.dll",StringMarshalling=StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint owner,string text,string caption,uint type);

    [LibraryImport("kernel32.dll",StringMarshalling=StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint window,uint attribute,void* value,uint size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint window,in MARGINS margins);

    [LibraryImport("shell32.dll",StringMarshalling=StringMarshalling.Utf16)]
    public static partial nint ShellExecuteW(nint owner,string? operation,string file,string? parameters,string? directory,int show);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved,uint flags);

    [LibraryImport("comdlg32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetOpenFileNameW(ref OPENFILENAMEW options);

    public static int Scale(int value,uint dpi)=>(int)Math.Round(value*dpi/96.0);
}
