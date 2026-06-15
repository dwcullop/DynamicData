// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;

namespace DynamicData.List.Internal;

/// <summary>
/// Wraps each item in a source list with an <see cref="IListSlot{T}"/> handle. Maintains a parallel
/// mirror of the source so each slot's <see cref="IListSlot{T}.CurrentIndex"/> stays in sync as the
/// source mutates. Each slot is released when its item leaves the source via Remove, RemoveRange,
/// Clear, or Replace.
/// </summary>
/// <typeparam name="T">The source item type.</typeparam>
internal sealed class SlotAllocator<T>
    where T : notnull
{
    private readonly IObservable<IChangeSet<T>> _source;

    public SlotAllocator(IObservable<IChangeSet<T>> source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<IChangeSet<IListSlot<T>>> Run() =>
        Observable.Create<IChangeSet<IListSlot<T>>>(observer =>
        {
            var slots = new List<Slot<T>>();
            return _source.Subscribe(
                onNext: changes =>
                {
                    try
                    {
                        observer.OnNext(Process(slots, changes));
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                },
                onError: observer.OnError,
                onCompleted: observer.OnCompleted);
        });

    private static ChangeSet<IListSlot<T>> Process(List<Slot<T>> slots, IChangeSet<T> changes)
    {
        var output = new ChangeSet<IListSlot<T>>(changes.Count);
        foreach (var change in changes)
        {
            output.Add(ProcessOne(slots, change));
        }

        return output;
    }

    private static Change<IListSlot<T>> ProcessOne(List<Slot<T>> slots, Change<T> change)
    {
        switch (change.Reason)
        {
            case ListChangeReason.Add:
            {
                var index = change.Item.CurrentIndex >= 0 ? change.Item.CurrentIndex : slots.Count;
                var slot = new Slot<T>(change.Item.Current, index);
                slots.Insert(index, slot);
                ShiftIndices(slots, index + 1, +1);
                return new Change<IListSlot<T>>(ListChangeReason.Add, slot, index);
            }

            case ListChangeReason.AddRange:
            {
                var index = change.Range.Index >= 0 ? change.Range.Index : slots.Count;
                var count = change.Range.Count;
                var newSlots = new List<Slot<T>>(count);
                var i = 0;
                foreach (var item in change.Range)
                {
                    newSlots.Add(new Slot<T>(item, index + i));
                    i++;
                }

                slots.InsertRange(index, newSlots);
                ShiftIndices(slots, index + count, +count);
                return new Change<IListSlot<T>>(ListChangeReason.AddRange, newSlots, index);
            }

            case ListChangeReason.Replace:
            {
                var prevIndex = change.Item.PreviousIndex;
                var curIndex = change.Item.CurrentIndex;
                var oldSlot = slots[prevIndex];
                oldSlot.Release();

                if (curIndex == prevIndex)
                {
                    var newSlot = new Slot<T>(change.Item.Current, curIndex);
                    slots[curIndex] = newSlot;
                    return new Change<IListSlot<T>>(ListChangeReason.Replace, newSlot, Optional.Some<IListSlot<T>>(oldSlot), curIndex, prevIndex);
                }
                else
                {
                    slots.RemoveAt(prevIndex);
                    ShiftIndices(slots, prevIndex, -1);

                    var insertAt = curIndex;
                    var newSlot = new Slot<T>(change.Item.Current, insertAt);
                    slots.Insert(insertAt, newSlot);
                    ShiftIndices(slots, insertAt + 1, +1);
                    return new Change<IListSlot<T>>(ListChangeReason.Replace, newSlot, Optional.Some<IListSlot<T>>(oldSlot), curIndex, prevIndex);
                }
            }

            case ListChangeReason.Remove:
            {
                var index = change.Item.CurrentIndex;
                var slot = slots[index];
                slot.Release();
                slots.RemoveAt(index);
                ShiftIndices(slots, index, -1);
                return new Change<IListSlot<T>>(ListChangeReason.Remove, slot, index);
            }

            case ListChangeReason.RemoveRange:
            {
                var index = change.Range.Index;
                var count = change.Range.Count;
                var removed = new List<IListSlot<T>>(count);
                for (var i = 0; i < count; i++)
                {
                    var s = slots[index];
                    s.Release();
                    removed.Add(s);
                    slots.RemoveAt(index);
                }

                ShiftIndices(slots, index, -count);
                return new Change<IListSlot<T>>(ListChangeReason.RemoveRange, removed, index);
            }

            case ListChangeReason.Clear:
            {
                var cleared = new List<IListSlot<T>>(slots.Count);
                foreach (var s in slots)
                {
                    s.Release();
                    cleared.Add(s);
                }

                slots.Clear();
                return new Change<IListSlot<T>>(ListChangeReason.Clear, cleared);
            }

            case ListChangeReason.Refresh:
            {
                var index = change.Item.CurrentIndex;
                var slot = slots[index];
                return new Change<IListSlot<T>>(ListChangeReason.Refresh, slot, index);
            }

            case ListChangeReason.Moved:
            {
                var prevIndex = change.Item.PreviousIndex;
                var curIndex = change.Item.CurrentIndex;
                var slot = slots[prevIndex];
                slots.RemoveAt(prevIndex);
                slots.Insert(curIndex, slot);
                ReindexBetween(slots, Math.Min(prevIndex, curIndex), Math.Max(prevIndex, curIndex));
                return new Change<IListSlot<T>>(slot, curIndex, prevIndex);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(change), change.Reason, "Unsupported list change reason");
        }
    }

    private static void ShiftIndices(List<Slot<T>> slots, int from, int delta)
    {
        for (var i = from; i < slots.Count; i++)
        {
            slots[i].CurrentIndex += delta;
        }
    }

    private static void ReindexBetween(List<Slot<T>> slots, int from, int to)
    {
        for (var i = from; i <= to && i < slots.Count; i++)
        {
            slots[i].CurrentIndex = i;
        }
    }
}
