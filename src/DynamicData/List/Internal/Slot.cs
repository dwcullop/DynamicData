// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace DynamicData.List.Internal;

/// <summary>
/// Concrete <see cref="IListSlot{T}"/> implementation. Reference identity; mutable CurrentIndex
/// is updated by the slot owner (<see cref="SlotAllocator{T}"/>).
/// </summary>
internal sealed class Slot<T> : IListSlot<T>, IDisposable
    where T : notnull
{
    private readonly Subject<Unit> _released = new();
    private int _currentIndex;
    private bool _isReleased;

    public Slot(T item, int initialIndex)
    {
        Item = item;
        _currentIndex = initialIndex;
    }

    public T Item { get; }

    public int CurrentIndex
    {
        get => _currentIndex;
        internal set => _currentIndex = value;
    }

    public bool IsReleased => _isReleased;

    // Honor the IListSlot<T>.Released contract for both early and late subscribers:
    //   - Early subscribers receive OnNext + OnCompleted when Release() fires on the live subject.
    //   - Late subscribers (attached after Release) hit the short-circuit and receive
    //     OnNext + OnCompleted synchronously on subscribe.
    // This avoids AsyncSubject<Unit>.Dispose, which would cause late subscriptions to throw
    // ObjectDisposedException while still satisfying CA2213 because the live subject IS disposed
    // in Release() once subscribers have been signalled.
    public IObservable<Unit> Released => Observable.Create<Unit>(observer =>
    {
        if (_isReleased)
        {
            observer.OnNext(Unit.Default);
            observer.OnCompleted();
            return Disposable.Empty;
        }

        return _released.SubscribeSafe(observer);
    });

    public bool Equals(IListSlot<T>? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override string ToString() => $"Slot[{(_isReleased ? "released" : _currentIndex.ToString())}] {Item}";

    public void Dispose() => Release();

    internal void Release()
    {
        if (_isReleased) return;

        // Set the release flag BEFORE notifying so any subscriber that races into the Released
        // property after this point sees the flag and takes the short-circuit instead of
        // attempting to subscribe to the soon-to-be-disposed Subject.
        _isReleased = true;
        _currentIndex = -1;
        _released.OnNext(Unit.Default);
        _released.OnCompleted();
        _released.Dispose();
    }
}
