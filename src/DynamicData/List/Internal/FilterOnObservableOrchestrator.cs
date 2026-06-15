// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Orchestrator for the non-buffered FilterOnObservable shape: per source slot, track a
/// boolean filter observable. Source items default to included; filter observable emissions
/// transition the included state. Emits an intermediate <see cref="IChangeSet{T}"/> of
/// <see cref="ObjWithFilterValue{T}"/> wrappers; the operator's Run method wraps the
/// orchestrator output with the existing Filter+Transform+SuppressRefresh pipeline.
/// </summary>
internal sealed class FilterOnObservableOrchestrator<TObject> : IListOrchestrator<TObject, bool, IChangeSet<ObjWithFilterValue<TObject>>>
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<bool>> _filter;
    private readonly Dictionary<IListSlot<TObject>, ObjWithFilterValue<TObject>> _slotToWrapper = new();
    private readonly List<Change<ObjWithFilterValue<TObject>>> _pending = new();

    public FilterOnObservableOrchestrator(Func<TObject, IObservable<bool>> filter) =>
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));

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
                    _pending.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Add, wrapper, slot.CurrentIndex));
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

                    _pending.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.AddRange, wrappers, change.Range.Index));

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

                    _pending.Add(new Change<ObjWithFilterValue<TObject>>(
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
                        _pending.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Remove, wrapper, change.Item.CurrentIndex));
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

                    _pending.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.RemoveRange, wrappers, change.Range.Index));
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
                    _pending.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Clear, wrappers));
                    break;
                }

                case ListChangeReason.Refresh:
                {
                    var slot = change.Item.Current;
                    if (_slotToWrapper.TryGetValue(slot, out var wrapper))
                    {
                        _pending.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Refresh, wrapper, Optional<ObjWithFilterValue<TObject>>.None, change.Item.CurrentIndex));
                    }

                    break;
                }

                case ListChangeReason.Moved:
                {
                    var slot = change.Item.Current;
                    if (_slotToWrapper.TryGetValue(slot, out var wrapper))
                    {
                        _pending.Add(new Change<ObjWithFilterValue<TObject>>(wrapper, change.Item.CurrentIndex, change.Item.PreviousIndex));
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

        // The filter observable emits a new boolean; produce a Refresh with the updated
        // filter state so the downstream Filter operator can transition Add/Remove.
        var refreshed = new ObjWithFilterValue<TObject>(slot.Item, value);
        _slotToWrapper[slot] = refreshed;
        _pending.Add(new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Refresh, refreshed, Optional<ObjWithFilterValue<TObject>>.None, slot.CurrentIndex));
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<ObjWithFilterValue<TObject>>> emitter)
    {
        if (_pending.Count == 0) return;

        var snapshot = new ChangeSet<ObjWithFilterValue<TObject>>(_pending);
        _pending.Clear();
        emitter.OnNext(snapshot);
    }
}
