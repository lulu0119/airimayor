using System;

namespace System.Windows.Threading
{
    public enum DispatcherPriority
    {
        Invalid = -1,
        Inactive = 0,
        SystemIdle = 1,
        ApplicationIdle = 2,
        ContextIdle = 3,
        Background = 4,
        Input = 5,
        Loaded = 6,
        Render = 7,
        DataBind = 8,
        Normal = 9,
        Send = 10,
    }

    public sealed class DispatcherOperation
    {
    }

    public sealed class Dispatcher
    {
        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Delegate method, object arg)
        {
            return new DispatcherOperation();
        }
    }
}
