using System.Runtime.InteropServices;

namespace Momoko.Launcher.Host;

// The single launcher window: a captionless Win32 window (DWM keeps the shadow
// and Windows 11 rounded corners) filled by one WebView2. The page is embedded
// in the executable and served from memory through WebResourceRequested:
// https://ui.momoko.example/<path> is resource "ui/<path>". Game icons and
// backgrounds are https images named by the Momoko launcher settings.
internal sealed unsafe class HostWindow
{
    public static readonly string ClassName=App.LauncherApp.Developer?"MomokoLauncherDevWindow":"MomokoLauncherWindow";
    public const string UiHost="ui.momoko.example";
    private const int LogicalWidth=1280,LogicalHeight=720;
    private static HostWindow? current;

    private readonly string title,userDataFolder;
    private readonly bool developer;
    private nint handle;
    private ICoreWebView2Environment? environment;
    private ICoreWebView2Controller? controller;
    private ICoreWebView2? webView;
    private WebViewColor background;
    private bool dark;
    private bool minimized;

    public event Action<string>? MessageReceived;
    public event Action? CloseRequested;
    public event Action<string>? Failed;

    public nint Handle=>handle;
    public bool Ready=>webView is not null;

    public HostWindow(string title,string userDataFolder,bool developer)
    {
        this.title=title;this.userDataFolder=userDataFolder;this.developer=developer;
    }

    public void Create(bool dark)
    {
        current=this;
        var module=Win32.GetModuleHandleW(null);
        var icon=Win32.LoadIconW(module,Win32.IDI_APPLICATION);
        if(icon==0)icon=Win32.LoadIconW(0,Win32.IDI_APPLICATION);
        fixed(char* name=ClassName)
        {
            var windowClass=new Win32.WNDCLASSEXW
            {
                Size=(uint)sizeof(Win32.WNDCLASSEXW),
                WindowProc=&WindowProc,
                Instance=module,
                Icon=icon,
                SmallIcon=icon,
                Cursor=Win32.LoadCursorW(0,Win32.IDC_ARROW),
                ClassName=name
            };
            if(Win32.RegisterClassExW(in windowClass)==0)
                throw new InvalidOperationException("无法注册启动器窗口类，错误 "+Marshal.GetLastPInvokeError());
        }
        var dpi=Win32.GetDpiForSystem();
        var width=Win32.Scale(LogicalWidth,dpi);
        var height=Win32.Scale(LogicalHeight,dpi);
        var monitor=Win32.MonitorFromPoint(default,Win32.MONITOR_DEFAULTTOPRIMARY);
        var info=new Win32.MONITORINFO{Size=(uint)sizeof(Win32.MONITORINFO)};
        Win32.GetMonitorInfoW(monitor,ref info);
        var x=info.Work.Left+Math.Max(0,(info.Work.Width-width)/2);
        var y=info.Work.Top+Math.Max(0,(info.Work.Height-height)/2);
        handle=Win32.CreateWindowExW(Win32.WS_EX_APPWINDOW,ClassName,title,
            Win32.WS_POPUP|Win32.WS_CAPTION|Win32.WS_SYSMENU|Win32.WS_MINIMIZEBOX|Win32.WS_THICKFRAME|Win32.WS_CLIPCHILDREN,
            x,y,width,height,0,0,module,0);
        if(handle==0)throw new InvalidOperationException("无法创建启动器窗口，错误 "+Marshal.GetLastPInvokeError());
        var windowDpi=Win32.GetDpiForWindow(handle);
        if(windowDpi!=0&&windowDpi!=dpi)
        {
            width=Win32.Scale(LogicalWidth,windowDpi);height=Win32.Scale(LogicalHeight,windowDpi);
        }
        this.dark=dark;
        ApplyFrame(dark);
        Win32.SetWindowPos(handle,0,x,y,width,height,Win32.SWP_NOZORDER|Win32.SWP_FRAMECHANGED);
        background=dark?new WebViewColor{A=255,R=0x0B,G=0x0C,B=0x10}:new WebViewColor{A=255,R=0xF3,G=0xF4,B=0xF7};
    }

    // Keeps DWM's shadow and rounded corners while the page draws its own
    // 1px edge; dark mode only affects the shadow tint.
    public void ApplyFrame(bool dark)
    {
        var corner=Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(handle,Win32.DWMWA_WINDOW_CORNER_PREFERENCE,&corner,sizeof(int));
        var border=Win32.DWMWA_COLOR_NONE;
        Win32.DwmSetWindowAttribute(handle,Win32.DWMWA_BORDER_COLOR,&border,sizeof(uint));
        var darkMode=dark?1:0;
        Win32.DwmSetWindowAttribute(handle,Win32.DWMWA_USE_IMMERSIVE_DARK_MODE,&darkMode,sizeof(int));
        var margins=new Win32.MARGINS{Left=0,Right=0,Top=1,Bottom=0};
        Win32.DwmExtendFrameIntoClientArea(handle,in margins);
    }

    public void SetBackground(bool dark)
    {
        if(this.dark==dark&&webView is not null)return;
        this.dark=dark;
        background=dark?new WebViewColor{A=255,R=0x0B,G=0x0C,B=0x10}:new WebViewColor{A=255,R=0xF3,G=0xF4,B=0xF7};
        if(controller is ICoreWebView2Controller2 controller2)controller2.put_DefaultBackgroundColor(background);
        ApplyFrame(dark);
    }

    public void Show()
    {
        Win32.ShowWindow(handle,Win32.SW_SHOWNORMAL);
        Win32.SetForegroundWindow(handle);
    }

    public void StartWebView()
    {
        Directory.CreateDirectory(userDataFolder);
        var result=WebView2Loader.CreateCoreWebView2EnvironmentWithOptions(null,userDataFolder,0,
            new EnvironmentCompletedHandler(OnEnvironment));
        if(result<0)throw new WebViewUnavailableException(result);
    }

    private void OnEnvironment(int result,ICoreWebView2Environment? environment)
    {
        if(result<0||environment is null){Failed?.Invoke(WebViewUnavailableException.Describe(result));return;}
        this.environment=environment;
        environment.CreateCoreWebView2Controller(handle,new ControllerCompletedHandler(OnController));
    }

    private void OnController(int result,ICoreWebView2Controller? created)
    {
        if(result<0||created is null){Failed?.Invoke(WebViewUnavailableException.Describe(result));return;}
        controller=created;
        if(created is ICoreWebView2Controller2 controller2)controller2.put_DefaultBackgroundColor(background);
        var view=created.get_CoreWebView2();
        var settings=view.get_Settings();
        settings.put_IsStatusBarEnabled(0);
        settings.put_IsZoomControlEnabled(0);
        settings.put_AreHostObjectsAllowed(0);
        settings.put_AreDefaultScriptDialogsEnabled(0);
        settings.put_AreDevToolsEnabled(developer?1:0);
        settings.put_AreDefaultContextMenusEnabled(developer?1:0);
        if(settings is ICoreWebView2Settings3 settings3)settings3.put_AreBrowserAcceleratorKeysEnabled(developer?1:0);
        if(settings is ICoreWebView2Settings4 settings4)
        {
            settings4.put_IsPasswordAutosaveEnabled(0);
            settings4.put_IsGeneralAutofillEnabled(0);
        }
        if(settings is ICoreWebView2Settings5 settings5)settings5.put_IsPinchZoomEnabled(0);
        if(settings is ICoreWebView2Settings6 settings6)settings6.put_IsSwipeNavigationEnabled(0);
        if(settings is ICoreWebView2Settings9 settings9)settings9.put_IsNonClientRegionSupportEnabled(1);
        view.AddWebResourceRequestedFilter($"https://{UiHost}/*",WebView2Loader.WebResourceContextAll);
        view.add_WebResourceRequested(new WebResourceRequestedHandler(ServeEmbedded),out _);
        view.add_WebMessageReceived(new WebMessageReceivedHandler(json=>MessageReceived?.Invoke(json)),out _);
        view.add_NewWindowRequested(new NewWindowRequestedHandler(OpenExternal),out _);
        webView=view;
        minimized=Win32.IsIconic(handle);
        Resize();
        created.put_IsVisible(minimized?0:1);
        view.Navigate($"https://{UiHost}/index.html?theme={(dark?"dark":"light")}");
        if(Win32.GetActiveWindow()==handle)Focus();
    }

    // WebView2 rejects MoveFocus (E_INVALIDARG) while it is hidden, and the
    // WebView is hidden whenever the window is minimized.
    private void Focus()
    {
        if(controller is null||minimized)return;
        controller.MoveFocus(WebView2Loader.MoveFocusProgrammatic);
    }

    // Minimizing hides the WebView; restoring shows it again and, for the
    // active window, gives it the keyboard focus that WM_ACTIVATE could not
    // (activation of a minimized window arrives before the restore).
    private void OnSize(nint kind)
    {
        var nowMinimized=kind==Win32.SIZE_MINIMIZED;
        var changed=nowMinimized!=minimized;
        minimized=nowMinimized;
        if(changed)controller?.put_IsVisible(minimized?0:1);
        if(minimized)return;
        Resize();
        if(changed&&Win32.GetActiveWindow()==handle)Focus();
    }

    private void ServeEmbedded(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        if(environment is null)return;
        var request=args.get_Request();
        var name=ResourceName(request.get_Uri());
        var data=name is null||request.get_Method()!="GET"?null:Core.Embedded.Read(name);
        nint stream=0,response=0;
        try
        {
            if(data is null)
            {
                environment.CreateWebResourceResponse(0,404,"Not Found","Cache-Control: no-store",out response);
            }
            else
            {
                fixed(byte* bytes=data)stream=Win32.SHCreateMemStream(bytes,(uint)data.Length);
                if(stream==0)throw new OutOfMemoryException("无法为内置文件创建内存流："+name);
                environment.CreateWebResourceResponse(stream,200,"OK",$"Content-Type: {ContentType(name!)}\r\nCache-Control: no-store",out response);
            }
            args.put_Response(response);
        }
        finally
        {
            if(response!=0)Marshal.Release(response);
            if(stream!=0)Marshal.Release(stream);
        }
    }

    private static string? ResourceName(string url)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")return null;
        var path=Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if(path.Length==0||path.Contains("..",StringComparison.Ordinal)||path.Contains('\\'))return null;
        return uri.Host==UiHost?"ui/"+path:null;
    }

    private static string ContentType(string name)=>Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".html"=>"text/html; charset=utf-8",
        ".css"=>"text/css; charset=utf-8",
        ".js"=>"text/javascript; charset=utf-8",
        ".json"=>"application/json; charset=utf-8",
        ".md" or ".txt"=>"text/plain; charset=utf-8",
        ".webp"=>"image/webp",
        ".png"=>"image/png",
        ".svg"=>"image/svg+xml",
        ".ico"=>"image/x-icon",
        _=>"application/octet-stream"
    };

    public void Post(string json)
    {
        if(webView is null)return;
        webView.PostWebMessageAsJson(json);
    }

    public void Minimize()=>Win32.ShowWindow(handle,Win32.SW_MINIMIZE);

    public void Restore()
    {
        if(Win32.IsIconic(handle))Win32.ShowWindow(handle,Win32.SW_RESTORE);
        Win32.SetForegroundWindow(handle);
    }

    // After browser authorization: come back to the front, or flash the
    // taskbar button when Windows does not allow taking the focus.
    public void Attention()
    {
        if(Win32.IsIconic(handle))Win32.ShowWindow(handle,Win32.SW_RESTORE);
        if(!Win32.SetForegroundWindow(handle))Win32.FlashWindow(handle,true);
    }

    // Fallback for runtimes without app-region support.
    public void BeginDrag()
    {
        Win32.ReleaseCapture();
        Win32.SendMessageW(handle,Win32.WM_NCLBUTTONDOWN,Win32.HTCAPTION,0);
    }

    public void Destroy()
    {
        controller?.Close();
        controller=null;webView=null;
        Win32.DestroyWindow(handle);
    }

    public static void OpenExternal(string target)
    {
        if(!Uri.TryCreate(target,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))return;
        Win32.ShellExecuteW(0,"open",uri.AbsoluteUri,null,null,Win32.SW_SHOWNORMAL);
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Win32.ShellExecuteW(0,"open",folder,null,null,Win32.SW_SHOWNORMAL);
    }

    public static int RunMessageLoop()
    {
        while(Win32.GetMessageW(out var message,0,0,0)>0)
        {
            Win32.TranslateMessage(in message);
            Win32.DispatchMessageW(in message);
        }
        return 0;
    }

    private void Resize()
    {
        if(controller is null)return;
        Win32.GetClientRect(handle,out var bounds);
        controller.put_Bounds(bounds);
    }

    private nint Process(nint window,uint message,nint wParam,nint lParam)
    {
        switch(message)
        {
            case Win32.WM_NCCALCSIZE when wParam!=0:
                return 0;
            case Win32.WM_NCHITTEST:
                return Win32.HTCLIENT;
            case Win32.WM_SIZE:
                OnSize(wParam);
                return 0;
            case Win32.WM_MOVE:
                controller?.NotifyParentWindowPositionChanged();
                return 0;
            case Win32.WM_DPICHANGED:
            {
                var suggested=*(Win32.RECT*)lParam;
                Win32.SetWindowPos(window,0,suggested.Left,suggested.Top,suggested.Width,suggested.Height,Win32.SWP_NOZORDER|Win32.SWP_NOACTIVATE);
                return 0;
            }
            case Win32.WM_ACTIVATE:
                // The high word is non-zero while the window is still minimized.
                if((wParam&0xFFFF)!=Win32.WA_INACTIVE&&((wParam>>16)&0xFFFF)==0)Focus();
                return 0;
            case Win32.WM_CLOSE:
                CloseRequested?.Invoke();
                return 0;
            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return 0;
            case UiDispatcher.Message:
                UiDispatcher.Instance.Drain();
                return 0;
        }
        return Win32.DefWindowProcW(window,message,wParam,lParam);
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window,uint message,nint wParam,nint lParam)
    {
        try
        {
            var target=current;
            if(target is null||(target.handle!=0&&target.handle!=window))return Win32.DefWindowProcW(window,message,wParam,lParam);
            if(target.handle==0)target.handle=window;
            return target.Process(window,message,wParam,lParam);
        }
        catch(Exception error)
        {
            Program.ReportUnhandled(error);
            return 0;
        }
    }
}

internal sealed class WebViewUnavailableException(int result):Exception(Describe(result))
{
    public static string Describe(int result)=>result==unchecked((int)0x80070002)
        ?"没有找到 Microsoft Edge WebView2 运行时。请先安装 WebView2 运行时（Windows 11 自带），再打开启动器。"
        :$"WebView2 初始化失败（0x{result:X8}）。";
}
