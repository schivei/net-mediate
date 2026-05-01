#if NETSTANDARD
#pragma warning disable IDE0130
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Threading
{
    public sealed class Lock
    {
        private ThreadId _owningThreadId;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Enter()
        {
            ThreadId currentThreadId = ThreadId.CurrentThreadId;
            Debug.Assert(currentThreadId.IsInitialized);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ThreadId EnterAndGetCurrentThreadId()
        {
            ThreadId currentThreadId = ThreadId.CurrentThreadId;
            Debug.Assert(currentThreadId.IsInitialized);
            Debug.Assert(currentThreadId.Id == _owningThreadId.Id);
            return currentThreadId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Scope EnterScope() => new(this, EnterAndGetCurrentThreadId());

        public void Exit()
        {
            ThreadId currentThreadId = ThreadId.CurrentThreadId;
            Debug.Assert(currentThreadId.IsInitialized);
            Debug.Assert(currentThreadId.Id == _owningThreadId.Id);
            _owningThreadId = ThreadId.Uninitialized;
        }

        public void Exit(ThreadId currentThreadId)
        {
            Debug.Assert(currentThreadId.IsInitialized);
            Debug.Assert(currentThreadId.Id == _owningThreadId.Id);
            _owningThreadId = ThreadId.Uninitialized;
        }

        public readonly struct ThreadId(int id)
        {
            public static readonly ThreadId Uninitialized = default;
            public bool IsInitialized => Id != 0;
            public int Id { get; } = id;

            public static ThreadId CurrentThreadId => new(Thread.CurrentThread.ManagedThreadId);
        }

        public ref struct Scope
        {
            private Lock? _lockObj;
            private readonly ThreadId _currentThreadId;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Scope(Lock lockObj, ThreadId currentThreadId)
            {
                _lockObj = lockObj;
                _currentThreadId = currentThreadId;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                Lock? lockObj = _lockObj;
                if (lockObj is not null)
                {
                    _lockObj = null;
                    lockObj.Exit(_currentThreadId);
                }
            }
        }
    }
}
#pragma warning restore IDE0130
#endif