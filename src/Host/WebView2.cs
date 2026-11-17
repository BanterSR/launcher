using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Momoko.Launcher.Host;

// WebView2 COM interfaces declared with the COM source generator, so the host
// works under NativeAOT without the managed WebView2 SDK. Vtable order follows
// WebView2.idl from Microsoft.Web.WebView2 1.0.4314.35. Methods the launcher
// never calls keep their slot with a pointer-sized signature.

[StructLayout(LayoutKind.Sequential)]
internal struct WebViewColor
{
    public byte A,R,G,B;
}

internal static partial class WebView2Loader
{
    // User build: WebView2LoaderStatic.lib is linked into the executable (DirectPInvoke);
    // developer build: WebView2Loader.dll is bundled into the single-file executable.
    [LibraryImport("WebView2Loader",StringMarshalling=StringMarshalling.Utf16)]
    public static partial int CreateCoreWebView2EnvironmentWithOptions(string? browserExecutableFolder,string userDataFolder,nint environmentOptions,
        [MarshalUsing(typeof(ComInterfaceMarshaller<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>))]
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler handler);

    public const int MoveFocusProgrammatic=0;
    public const int WebResourceContextAll=0;
    public const int HostResourceAccessAllow=1;
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("b96d755e-0319-4e92-a296-23436f46a1fc")]
internal partial interface ICoreWebView2Environment
{
    void CreateCoreWebView2Controller(nint parentWindow,ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);
    // content: IStream* (may be 0); response: ICoreWebView2WebResourceResponse*, owned by the caller.
    void CreateWebResourceResponse(nint content,int statusCode,string reasonPhrase,string headers,out nint response);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
internal partial interface ICoreWebView2Controller
{
    int get_IsVisible();
    void put_IsVisible(int isVisible);
    Win32.RECT get_Bounds();
    void put_Bounds(Win32.RECT bounds);
    double get_ZoomFactor();
    void put_ZoomFactor(double zoomFactor);
    void add_ZoomFactorChanged(nint handler,out long token);
    void remove_ZoomFactorChanged(long token);
    void SetBoundsAndZoomFactor(Win32.RECT bounds,double zoomFactor);
    void MoveFocus(int reason);
    void add_MoveFocusRequested(nint handler,out long token);
    void remove_MoveFocusRequested(long token);
    void add_GotFocus(nint handler,out long token);
    void remove_GotFocus(long token);
    void add_LostFocus(nint handler,out long token);
    void remove_LostFocus(long token);
    void add_AcceleratorKeyPressed(nint handler,out long token);
    void remove_AcceleratorKeyPressed(long token);
    nint get_ParentWindow();
    void put_ParentWindow(nint parentWindow);
    void NotifyParentWindowPositionChanged();
    void Close();
    ICoreWebView2 get_CoreWebView2();
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("c979903e-d4ca-4228-92eb-47ee3fa96eab")]
internal partial interface ICoreWebView2Controller2:ICoreWebView2Controller
{
    WebViewColor get_DefaultBackgroundColor();
    void put_DefaultBackgroundColor(WebViewColor value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("76eceacb-0462-4d94-ac83-423a6793775e")]
internal partial interface ICoreWebView2
{
    ICoreWebView2Settings get_Settings();
    string get_Source();
    void Navigate(string uri);
    void NavigateToString(string htmlContent);
    void add_NavigationStarting(nint handler,out long token);
    void remove_NavigationStarting(long token);
    void add_ContentLoading(nint handler,out long token);
    void remove_ContentLoading(long token);
    void add_SourceChanged(nint handler,out long token);
    void remove_SourceChanged(long token);
    void add_HistoryChanged(nint handler,out long token);
    void remove_HistoryChanged(long token);
    void add_NavigationCompleted(nint handler,out long token);
    void remove_NavigationCompleted(long token);
    void add_FrameNavigationStarting(nint handler,out long token);
    void remove_FrameNavigationStarting(long token);
    void add_FrameNavigationCompleted(nint handler,out long token);
    void remove_FrameNavigationCompleted(long token);
    void add_ScriptDialogOpening(nint handler,out long token);
    void remove_ScriptDialogOpening(long token);
    void add_PermissionRequested(nint handler,out long token);
    void remove_PermissionRequested(long token);
    void add_ProcessFailed(nint handler,out long token);
    void remove_ProcessFailed(long token);
    void AddScriptToExecuteOnDocumentCreated(string javaScript,nint handler);
    void RemoveScriptToExecuteOnDocumentCreated(string id);
    void ExecuteScript(string javaScript,nint handler);
    void CapturePreview(int imageFormat,nint imageStream,nint handler);
    void Reload();
    void PostWebMessageAsJson(string webMessageAsJson);
    void PostWebMessageAsString(string webMessageAsString);
    void add_WebMessageReceived(ICoreWebView2WebMessageReceivedEventHandler handler,out long token);
    void remove_WebMessageReceived(long token);
    void CallDevToolsProtocolMethod(string methodName,string parametersAsJson,nint handler);
    uint get_BrowserProcessId();
    int get_CanGoBack();
    int get_CanGoForward();
    void GoBack();
    void GoForward();
    nint GetDevToolsProtocolEventReceiver(string eventName);
    void Stop();
    void add_NewWindowRequested(ICoreWebView2NewWindowRequestedEventHandler handler,out long token);
    void remove_NewWindowRequested(long token);
    void add_DocumentTitleChanged(nint handler,out long token);
    void remove_DocumentTitleChanged(long token);
    string get_DocumentTitle();
    void AddHostObjectToScript(string name,nint value);
    void RemoveHostObjectFromScript(string name);
    void OpenDevToolsWindow();
    void add_ContainsFullScreenElementChanged(nint handler,out long token);
    void remove_ContainsFullScreenElementChanged(long token);
    int get_ContainsFullScreenElement();
    void add_WebResourceRequested(ICoreWebView2WebResourceRequestedEventHandler handler,out long token);
    void remove_WebResourceRequested(long token);
    void AddWebResourceRequestedFilter(string uri,int resourceContext);
    void RemoveWebResourceRequestedFilter(string uri,int resourceContext);
    void add_WindowCloseRequested(nint handler,out long token);
    void remove_WindowCloseRequested(long token);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("9e8f0cf8-e670-4b5e-b2bc-73e061e3184c")]
internal partial interface ICoreWebView2_2:ICoreWebView2
{
    void add_WebResourceResponseReceived(nint handler,out long token);
    void remove_WebResourceResponseReceived(long token);
    void NavigateWithWebResourceRequest(nint request);
    void add_DOMContentLoaded(nint handler,out long token);
    void remove_DOMContentLoaded(long token);
    nint get_CookieManager();
    nint get_Environment();
}

// SetVirtualHostNameToFolderMapping serves a local folder under an https host
// (with range requests, so cached videos play).
[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("a0d6df20-3b92-416d-aa0c-437a9c727857")]
internal partial interface ICoreWebView2_3:ICoreWebView2_2
{
    void TrySuspend(nint handler);
    void Resume();
    int get_IsSuspended();
    void SetVirtualHostNameToFolderMapping(string hostName,string folderPath,int accessKind);
    void ClearVirtualHostNameToFolderMapping(string hostName);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("e562e4f0-d7fa-43ac-8d71-c05150499f00")]
internal partial interface ICoreWebView2Settings
{
    int get_IsScriptEnabled();
    void put_IsScriptEnabled(int value);
    int get_IsWebMessageEnabled();
    void put_IsWebMessageEnabled(int value);
    int get_AreDefaultScriptDialogsEnabled();
    void put_AreDefaultScriptDialogsEnabled(int value);
    int get_IsStatusBarEnabled();
    void put_IsStatusBarEnabled(int value);
    int get_AreDevToolsEnabled();
    void put_AreDevToolsEnabled(int value);
    int get_AreDefaultContextMenusEnabled();
    void put_AreDefaultContextMenusEnabled(int value);
    int get_AreHostObjectsAllowed();
    void put_AreHostObjectsAllowed(int value);
    int get_IsZoomControlEnabled();
    void put_IsZoomControlEnabled(int value);
    int get_IsBuiltInErrorPageEnabled();
    void put_IsBuiltInErrorPageEnabled(int value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("ee9a0f68-f46c-4e32-ac23-ef8cac224d2a")]
internal partial interface ICoreWebView2Settings2:ICoreWebView2Settings
{
    string get_UserAgent();
    void put_UserAgent(string value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("fdb5ab74-af33-4854-84f0-0a631deb5eba")]
internal partial interface ICoreWebView2Settings3:ICoreWebView2Settings2
{
    int get_AreBrowserAcceleratorKeysEnabled();
    void put_AreBrowserAcceleratorKeysEnabled(int value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("cb56846c-4168-4d53-b04f-03b6d6796ff2")]
internal partial interface ICoreWebView2Settings4:ICoreWebView2Settings3
{
    int get_IsPasswordAutosaveEnabled();
    void put_IsPasswordAutosaveEnabled(int value);
    int get_IsGeneralAutofillEnabled();
    void put_IsGeneralAutofillEnabled(int value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("183e7052-1d03-43a0-ab99-98e043b66b39")]
internal partial interface ICoreWebView2Settings5:ICoreWebView2Settings4
{
    int get_IsPinchZoomEnabled();
    void put_IsPinchZoomEnabled(int value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("11cb3acd-9bc8-43b8-83bf-f40753714f87")]
internal partial interface ICoreWebView2Settings6:ICoreWebView2Settings5
{
    int get_IsSwipeNavigationEnabled();
    void put_IsSwipeNavigationEnabled(int value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("488dc902-35ef-42d2-bc7d-94b65c4bc49c")]
internal partial interface ICoreWebView2Settings7:ICoreWebView2Settings6
{
    int get_HiddenPdfToolbarItems();
    void put_HiddenPdfToolbarItems(int value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("9e6b0e8f-86ad-4e81-8147-a9b5edb68650")]
internal partial interface ICoreWebView2Settings8:ICoreWebView2Settings7
{
    int get_IsReputationCheckingRequired();
    void put_IsReputationCheckingRequired(int value);
}

// app-region: drag on the page moves the window (WebView2 runtime 1.0.2420+).
[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("0528a73b-e92d-49f4-927a-e547dddaa37d")]
internal partial interface ICoreWebView2Settings9:ICoreWebView2Settings8
{
    int get_IsNonClientRegionSupportEnabled();
    void put_IsNonClientRegionSupportEnabled(int value);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    void Invoke(int errorCode,ICoreWebView2Environment? result);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    void Invoke(int errorCode,ICoreWebView2Controller? result);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
internal partial interface ICoreWebView2WebMessageReceivedEventHandler
{
    void Invoke(ICoreWebView2 sender,ICoreWebView2WebMessageReceivedEventArgs args);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs
{
    string get_Source();
    string get_WebMessageAsJson();
    string TryGetWebMessageAsString();
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("d4c185fe-c81c-4989-97af-2d3fa7ab5651")]
internal partial interface ICoreWebView2NewWindowRequestedEventHandler
{
    void Invoke(ICoreWebView2 sender,ICoreWebView2NewWindowRequestedEventArgs args);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("34acb11c-fc37-4418-9132-f9c21d1eafb9")]
internal partial interface ICoreWebView2NewWindowRequestedEventArgs
{
    string get_Uri();
    void put_NewWindow(nint newWindow);
    nint get_NewWindow();
    void put_Handled(int handled);
    int get_Handled();
    int get_IsUserInitiated();
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("ab00b74c-15f1-4646-80e8-e76341d25d71")]
internal partial interface ICoreWebView2WebResourceRequestedEventHandler
{
    void Invoke(ICoreWebView2 sender,ICoreWebView2WebResourceRequestedEventArgs args);
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("453e667f-12c7-49d4-be6d-ddbe7956f57a")]
internal partial interface ICoreWebView2WebResourceRequestedEventArgs
{
    ICoreWebView2WebResourceRequest get_Request();
    nint get_Response();
    void put_Response(nint response);
    nint GetDeferral();
    int get_ResourceContext();
}

[GeneratedComInterface(StringMarshalling=StringMarshalling.Utf16)]
[Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5")]
internal partial interface ICoreWebView2WebResourceRequest
{
    string get_Uri();
    void put_Uri(string uri);
    string get_Method();
    void put_Method(string method);
    nint get_Content();
    void put_Content(nint content);
    nint get_Headers();
}

[GeneratedComClass]
internal sealed partial class EnvironmentCompletedHandler(Action<int,ICoreWebView2Environment?> callback)
    :ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
{
    public void Invoke(int errorCode,ICoreWebView2Environment? result)=>callback(errorCode,result);
}

[GeneratedComClass]
internal sealed partial class ControllerCompletedHandler(Action<int,ICoreWebView2Controller?> callback)
    :ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
{
    public void Invoke(int errorCode,ICoreWebView2Controller? result)=>callback(errorCode,result);
}

[GeneratedComClass]
internal sealed partial class WebMessageReceivedHandler(Action<string> callback):ICoreWebView2WebMessageReceivedEventHandler
{
    public void Invoke(ICoreWebView2 sender,ICoreWebView2WebMessageReceivedEventArgs args)=>callback(args.get_WebMessageAsJson());
}

[GeneratedComClass]
internal sealed partial class NewWindowRequestedHandler(Action<string> callback):ICoreWebView2NewWindowRequestedEventHandler
{
    public void Invoke(ICoreWebView2 sender,ICoreWebView2NewWindowRequestedEventArgs args)
    {
        args.put_Handled(1);
        callback(args.get_Uri());
    }
}

[GeneratedComClass]
internal sealed partial class WebResourceRequestedHandler(Action<ICoreWebView2WebResourceRequestedEventArgs> callback)
    :ICoreWebView2WebResourceRequestedEventHandler
{
    public void Invoke(ICoreWebView2 sender,ICoreWebView2WebResourceRequestedEventArgs args)=>callback(args);
}
