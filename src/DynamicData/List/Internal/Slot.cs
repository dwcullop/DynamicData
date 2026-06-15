// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive;
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
    private readonly AsyncSubject<Unit> _released = new();
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

    public IObservable<Unit> Released => _released;

    public bool Equals(IListSlot<T>? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override string ToString() => $"Slot[{(_isReleased ? "released" : _currentIndex.ToString())}] {Item}";

    public void Dispose() => Release();

    internal void Release()
    {
        if (_isReleased) return;

        _isReleased = true;
        _currentIndex = -1;
        _released.OnNext(Unit.Default);
        _released.OnCompleted();
        _released.Dispose();
    }
}
