using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Momoko.Launcher.Host;

// Runs continuations on the window thread: Post queues the callback and wakes
// the message loop, which drains the queue on DispatchMessage.
internal sealed class UiDispatcher:SynchronizationContext
{
    public const uint Message=Win32.WM_APP+1;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback,object? State)> queue=new();
    private readonly int threadId=Environment.CurrentManagedThreadId;
    private nint window;

    public static UiDispatcher Instance { get; private set; } = null!;

    public static UiDispatcher Install()
    {
        Instance=new UiDispatcher();
        SetSynchronizationContext(Instance);
        return Instance;
    }

    public void Attach(nint target)
    {
        window=target;
        if(!queue.IsEmpty)Win32.PostMessageW(window,Message,0,0);
    }

    public bool OnUiThread=>Environment.CurrentManagedThreadId==threadId;

    public override void Post(SendOrPostCallback callback,object? state)
    {
        queue.Enqueue((callback,state));
        if(window!=0)Win32.PostMessageW(window,Message,0,0);
    }

    public override void Send(SendOrPostCallback callback,object? state)
    {
        if(OnUiThread){callback(state);return;}
        using var done=new ManualResetEventSlim();
        ExceptionDispatchInfo? failure=null;
        Post(_=>
        {
            try{callback(state);}
            catch(Exception error){failure=ExceptionDispatchInfo.Capture(error);}
            finally{done.Set();}
        },null);
        done.Wait();
        failure?.Throw();
    }

    public override SynchronizationContext CreateCopy()=>this;

    public void Drain()
    {
        while(queue.TryDequeue(out var item))
        {
            try{item.Callback(item.State);}
            catch(Exception error){Program.ReportUnhandled(error);}
        }
    }

    public void Run(Action action)=>Post(_=>action(),null);
}
