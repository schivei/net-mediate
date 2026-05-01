#if NETSTANDARD || NET8_0
#pragma warning disable IDE0130
// System.Threading.Lock was introduced in .NET 9.  For net8.0 and netstandard targets we
// provide a drop-in polyfill backed by Monitor so that the C# 13 `lock(obj)` statement
// — which the compiler lowers to `obj.EnterScope()` when the type is System.Threading.Lock —
// still provides correct mutual exclusion.
using System.Runtime.CompilerServices;

namespace System.Threading
{
    /// <summary>
    /// Polyfill of <c>System.Threading.Lock</c> for targets earlier than .NET 9.
    /// Uses <see cref="Monitor"/> internally so that the C# 13 <c>lock</c> statement
    /// lowers to <see cref="EnterScope"/> and still provides proper mutual exclusion.
    /// </summary>
    public sealed class Lock
    {
        /// <summary>Acquires the lock and returns a <see cref="Scope"/> that releases it on disposal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Scope EnterScope()
        {
            Monitor.Enter(this);
            return new Scope(this);
        }

        /// <summary>
        /// A ref struct returned by <see cref="EnterScope"/> that releases the lock when disposed.
        /// Mirrors the shape of the .NET 9 <c>Lock.Scope</c> so that <c>using var _ = lock.EnterScope()</c>
        /// compiles and behaves identically across all TFMs.
        /// </summary>
        public ref struct Scope
        {
            private Lock? _lockObj;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Scope(Lock lockObj) => _lockObj = lockObj;

            /// <summary>Releases the lock.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                Lock? lockObj = _lockObj;
                if (lockObj is not null)
                {
                    _lockObj = null;
                    Monitor.Exit(lockObj);
                }
            }
        }
    }
}
#pragma warning restore IDE0130
#endif
