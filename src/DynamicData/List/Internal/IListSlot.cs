// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive;

namespace DynamicData.List.Internal;

/// <summary>
/// An opaque, stable handle for one occurrence of an item in a list source.
/// Lists allow duplicates: each Add allocates a distinct slot, even if the items compare equal.
/// </summary>
/// <typeparam name="T">The source item type.</typeparam>
/// <remarks>
/// Slot identity is reference-based, independent of position. A slot survives Moves, Refreshes,
/// and Replaces of other items in the source. The slot is released when its own item leaves
/// the source via Remove, RemoveRange, Clear, or Replace.
/// </remarks>
internal interface IListSlot<T> : IEquatable<IListSlot<T>>
    where T : notnull
{
    /// <summary>Gets the item this slot represents. Immutable for the slot's lifetime.</summary>
    T Item { get; }

    /// <summary>
    /// Gets the current 0-based index of the item in the source. Updated by the slot owner on
    /// inserts, removes, and moves that shift this slot. Returns -1 after release.
    /// </summary>
    int CurrentIndex { get; }

    /// <summary>Gets a value indicating whether the slot has been released.</summary>
    bool IsReleased { get; }

    /// <summary>
    /// Gets an observable that fires exactly once with <see cref="Unit.Default"/> when the slot
    /// is released, then completes. Subscribers that attach after release receive the
    /// notification synchronously on subscribe.
    /// </summary>
    IObservable<Unit> Released { get; }
}
