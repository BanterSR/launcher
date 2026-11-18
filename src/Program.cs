using Momoko.Launcher.App;
using Momoko.Launcher.Core;
using Momoko.Launcher.Host;

namespace Momoko.Launcher;

internal static class Program
{
    private static readonly string MutexName=LauncherApp.Developer?@"Local\MomokoLauncher.Dev":@"Local\MomokoLauncher";

    [STAThread]
    private static int Main(string[] arguments)
    {
        Win32.CoInitializeEx(0,Win32.COINIT_APARTMENTTHREADED);
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>ReportUnhandled(e.ExceptionObject as Exception??new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException+=(_,e)=>{ReportUnhandled(e.Exception);e.SetObserved();};
        // Started by a one-click update: the previous launcher still holds the mutex.
        Services.UpdateClient.WaitForPrevious(arguments);
        using var single=new Mutex(true,MutexName,out var created);
        if(!created)
        {
            // SW_RESTORE also shows a window hidden in the notification area.
            var existing=Win32.FindWindowW(HostWindow.ClassName,null);
            if(existing!=0)
            {
                Win32.ShowWindow(existing,Win32.SW_RESTORE);
                Win32.SetForegroundWindow(existing);
            }
            return 0;
        }
        Services.UpdateClient.CleanupPrevious();
        try
        {
            var dispatcher=UiDispatcher.Install();
            var momoko=Origins.Normalize(BuildInfo.MomokoOrigin,"内置 Momoko 地址");
            var settings=LauncherSettings.Load();
            settings.Save();
            var dark=settings.Theme=="dark"||(settings.Theme=="system"&&Dialogs.SystemDark());
            var window=new HostWindow(LauncherApp.Developer?"Momoko 启动器 (开发版)":"Momoko 启动器",AppPaths.WebView,LauncherApp.Developer);
            window.Create(dark);
            dispatcher.Attach(window.Handle);
            var app=new LauncherApp(window,momoko,settings);
            app.Start();
            LauncherLog.Write($"启动器就绪（{(LauncherApp.Developer?"开发版":"用户版")}），Momoko={momoko}");
            window.Show();
            window.StartWebView();
            return HostWindow.RunMessageLoop();
        }
        catch(Exception error)
        {
            LauncherLog.Error(error.ToString());
            Dialogs.Error(0,error.Message);
            return 1;
        }
    }

    public static void ReportUnhandled(Exception error)=>LauncherLog.Error("未处理的异常："+error);
}
