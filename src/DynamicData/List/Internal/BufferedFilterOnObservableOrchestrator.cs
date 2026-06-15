// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace DynamicData.List.Internal;

/// <summary>
/// Buffered variant of <see cref="FilterOnObservableOrchestrator{T}"/>: filter observable
/// emissions are buffered over a time window and emitted as a coalesced Refresh changeset
/// when the window expires. Source pass-through changes are unaffected; they emit on the
/// drain cycle that delivered them.
/// </summary>
internal sealed class BufferedFilterOnObservableOrchestrator<TObject>
    : IListOrchestrator<TObject, bool, IChangeSet<ObjWithFilterValue<TObject>>>, IDisposable
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<bool>> _filter;
    private readonly TimeSpan _buffer;
    private readonly IScheduler _scheduler;
    private readonly Subject<FilterUpdate> _filterUpdates = new();
    private readonly Dictionary<IListSlot<TObject>, ObjWithFilterValue<TObject>> _slotToWrapper = new();
    private readonly List<Change<ObjWithFilterValue<TObject>>> _pendingSourceChanges = new();

    public BufferedFilterOnObservableOrchestrator(
        Func<TObject, IObservable<bool>> filter,
        TimeSpan buffer,
        IScheduler scheduler,
        IListOrchestratorContext<TObject, bool> context,
        IObserver<IChangeSet<ObjWithFilterValue<TObject>>> emitter)
    {
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _buffer = buffer;
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        var batched = _filterUpdates
            .Buffer(_buffer, _scheduler)
            .Where(batch => batch.Count > 0);

        context.Serialize(batched).Subscribe(batch =>
        {
            var refreshes = new ChangeSet<ObjWithFilterValue<TObject>>(batch.Count);
            foreach (var update in batch)
            {
                if (update.Slot.IsReleased) continue;
                refreshes.Add(new Change<ObjWithFilterValue<TObject>>(
                    ListChangeReason.Refresh,
                    update.Wrapper,
                    Optional<ObjWithFilterValue<TObject>>.None,
                    update.Slot.CurrentIndex));
            }

            if (refreshes.Count > 0)
            {
                emitter.OnNext(refreshes);
            }
        });
    }

    public void OnSourceChangeSet(IChangeSet<IListSlot<TObject>> changes, IListOrchestratorContext<TObject, bool> context)
    {
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ListChangeReason.Add:
                {
                    var slot = change.Item.Current;
                    var wrapper = new ObjWithFilterValue<TObject>(slot.Item, true);
                    _slotToWrapper[slot] = wrapper;
                    _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Add, wrapper, slot.CurrentIndex));
                    context.Track(slot, _filter(slot.Item));
                    break;
                }

                case ListChangeReason.AddRange:
                {
                    var wrappers = new List<ObjWithFilterValue<TObject>>(change.Range.Count);
                    foreach (var slot in change.Range)
                    {
                        var wrapper = new ObjWithFilterValue<TObject>(slot.Item, true);
                        _slotToWrapper[slot] = wrapper;
                        wrappers.Add(wrapper);
                    }

                    _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.AddRange, wrappers, change.Range.Index));

                    foreach (var slot in change.Range)
                    {
                        context.Track(slot, _filter(slot.Item));
                    }

                    break;
                }

                case ListChangeReason.Replace:
                {
                    var oldSlot = change.Item.Previous.Value;
                    var newSlot = change.Item.Current;
                    var hadOldWrapper = _slotToWrapper.TryGetValue(oldSlot, out var oldWrapper);

                    var newWrapper = new ObjWithFilterValue<TObject>(newSlot.Item, true);
                    _slotToWrapper[newSlot] = newWrapper;
                    _slotToWrapper.Remove(oldSlot);

                    _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(
                        ListChangeReason.Replace,
                        newWrapper,
                        hadOldWrapper ? Optional.Some(oldWrapper) : Optional<ObjWithFilterValue<TObject>>.None,
                        change.Item.CurrentIndex,
                        change.Item.PreviousIndex));

                    context.Untrack(oldSlot);
                    context.Track(newSlot, _filter(newSlot.Item));
                    break;
                }

                case ListChangeReason.Remove:
                {
                    var slot = change.Item.Current;
                    if (_slotToWrapper.TryGetValue(slot, out var wrapper))
                    {
                        _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Remove, wrapper, change.Item.CurrentIndex));
                        _slotToWrapper.Remove(slot);
                    }

                    context.Untrack(slot);
                    break;
                }

                case ListChangeReason.RemoveRange:
                {
                    var wrappers = new List<ObjWithFilterValue<TObject>>(change.Range.Count);
                    foreach (var slot in change.Range)
                    {
                        if (_slotToWrapper.TryGetValue(slot, out var wrapper))
                        {
                            wrappers.Add(wrapper);
                            _slotToWrapper.Remove(slot);
                        }

                        context.Untrack(slot);
                    }

                    _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.RemoveRange, wrappers, change.Range.Index));
                    break;
                }

                case ListChangeReason.Clear:
                {
                    var wrappers = new List<ObjWithFilterValue<TObject>>(_slotToWrapper.Count);
                    foreach (var slot in change.Range)
                    {
                        if (_slotToWrapper.TryGetValue(slot, out var wrapper))
                        {
                            wrappers.Add(wrapper);
                        }

                        context.Untrack(slot);
                    }

                    _slotToWrapper.Clear();
                    _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Clear, wrappers));
                    break;
                }

                case ListChangeReason.Refresh:
                {
                    var slot = change.Item.Current;
                    if (_slotToWrapper.TryGetValue(slot, out var wrapper))
                    {
                        _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Refresh, wrapper, Optional<ObjWithFilterValue<TObject>>.None, change.Item.CurrentIndex));
                    }

                    break;
                }

                case ListChangeReason.Moved:
                {
                    var slot = change.Item.Current;
                    if (_slotToWrapper.TryGetValue(slot, out var wrapper))
                    {
                        _pendingSourceChanges.Add(new Change<ObjWithFilterValue<TObject>>(wrapper, change.Item.CurrentIndex, change.Item.PreviousIndex));
                    }

                    break;
                }
            }
        }
    }

    public void OnInner(bool value, IListSlot<TObject> slot, IObserver<IChangeSet<ObjWithFilterValue<TObject>>> emitter)
    {
        if (slot.IsReleased) return;
        if (!_slotToWrapper.TryGetValue(slot, out var existing)) return;

        var refreshed = new ObjWithFilterValue<TObject>(slot.Item, value);
        _slotToWrapper[slot] = refreshed;
        _filterUpdates.OnNext(new FilterUpdate(slot, refreshed));
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<ObjWithFilterValue<TObject>>> emitter)
    {
        if (_pendingSourceChanges.Count == 0) return;

        var snapshot = new ChangeSet<ObjWithFilterValue<TObject>>(_pendingSourceChanges);
        _pendingSourceChanges.Clear();
        emitter.OnNext(snapshot);
    }

    public void Dispose() => _filterUpdates.Dispose();

    private readonly record struct FilterUpdate(IListSlot<TObject> Slot, ObjWithFilterValue<TObject> Wrapper);
}
