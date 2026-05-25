// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData;

#if NET9_0_OR_GREATER

internal ref struct SwappableLock
{
    public static SwappableLock CreateAndEnter(Lock gate)
    {
        if (gate is null)
            throw new ArgumentNullException(nameof(gate));

        gate.Enter();
        return new SwappableLock { _gate = gate };
    }

    public void SwapTo(Lock gate)
    {
        if (gate is null)
            throw new ArgumentNullException(nameof(gate));

        if (_gate is null)
            throw new InvalidOperationException("Lock is not initialized");

        // Same-gate is a no-op. System.Threading.Lock is not reentrant on .NET 9, so a
        // naive re-acquire of the current gate would throw LockRecursionException.
        if (ReferenceEquals(gate, _gate))
            return;

        gate.Enter();

        // Exception safety: if _gate.Exit() throws (only possible on invariant violation,
        // since we hold _gate by construction), release the newly-acquired gate to avoid
        // leaking it on top of the already-corrupt state.
        try
        {
            _gate.Exit();
        }
        catch
        {
            gate.Exit();
            throw;
        }

        _gate = gate;
    }

    public void Dispose()
    {
        if (_gate is not null)
        {
            _gate.Exit();
            _gate = null;
        }
    }

    private Lock? _gate;
}

#else

internal ref struct SwappableLock
{
    public static SwappableLock CreateAndEnter(object gate)
    {
        if (gate is null)
            throw new ArgumentNullException(nameof(gate));

        var result = new SwappableLock()
        {
            _gate = gate
        };

        Monitor.Enter(gate, ref result._hasLock);

        return result;
    }

    public void SwapTo(object gate)
    {
        if (gate is null)
            throw new ArgumentNullException(nameof(gate));

        if (_gate is null)
            throw new InvalidOperationException("Lock is not initialized");

        // Same-gate is a no-op. Monitor is reentrant so this is harmless under the legacy
        // branch, but the fast-path avoids an unnecessary lock acquisition and matches the
        // NET9 branch semantics, where the same call would otherwise deadlock.
        if (ReferenceEquals(gate, _gate))
            return;

        var hasNewLock = false;
        Monitor.Enter(gate, ref hasNewLock);

        // Exception safety: if Monitor.Exit on the old gate throws (only possible on
        // invariant violation, since we hold _gate by construction), release the newly
        // acquired gate to avoid leaking it on top of the already-corrupt state.
        try
        {
            if (_hasLock)
            {
                Monitor.Exit(_gate);
            }
        }
        catch
        {
            if (hasNewLock)
            {
                try
                {
                    Monitor.Exit(gate);
                }
                catch
                {
                    // The world is already on fire; surface the original exception.
                }
            }

            throw;
        }

        _hasLock = hasNewLock;
        _gate = gate;
    }

    public void Dispose()
    {
        if (_hasLock && (_gate is not null))
        {
            Monitor.Exit(_gate);
            _hasLock = false;
            _gate = null;
        }
    }

    private bool _hasLock;
    private object? _gate;
}

#endif
