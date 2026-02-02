using System;
using System.Threading.Tasks;

namespace HttpLib
{
    public class ITask
    {
        public static Task Run(Action action, Action? end = null)
        {
            if (end == null)
            {
#if NET40
                return Task.Factory.StartNew(action);
#else
                return Task.Run(action);
#endif
            }
#if NET40
            return Task.Factory.StartNew(action).ContinueWith(action => { end(); });
#else
            return Task.Run(action).ContinueWith(action => { end(); });
#endif
        }
    }
}