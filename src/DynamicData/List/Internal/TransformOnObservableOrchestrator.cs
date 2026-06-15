// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Orchestrator for the TransformOnObservable shape: per source slot, track a transform
/// observable. Each emitted transform value becomes the slot's current transformed value;
/// the downstream pipeline filters out slots whose transform hasn't yet emitted.
/// </summary>
internal sealed class TransformOnObservableOrchestrator<TSource, TDestination>
    : IListOrchestrator<TSource, TDestination, IChangeSet<TransformedValue<TSource, TDestination>>>
    where TSource : notnull
    where TDestination : notnull
{
    private readonly Func<TSource, IObservable<TDestination>> _transform;
    private readonly Dictionary<IListSlot<TSource>, TransformedValue<TSource, TDestination>> _slotToValue = new();
    private readonly List<Change<TransformedValue<TSource, TDestination>>> _pending = new();

    public TransformOnObservableOrchestrator(Func<TSource, IObservable<TDestination>> transform) =>
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));

    public void OnSourceChangeSet(IChangeSet<IListSlot<TSource>> changes, IListOrchestratorContext<TSource, TDestination> context)
    {
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ListChangeReason.Add:
                {
                    var slot = change.Item.Current;
                    var wrapper = new TransformedValue<TSource, TDestination>(slot.Item, Optional<TDestination>.None);
                    _slotToValue[slot] = wrapper;
                    _pending.Add(new Change<TransformedValue<TSource, TDestination>>(ListChangeReason.Add, wrapper, slot.CurrentIndex));
                    context.Track(slot, _transform(slot.Item));
                    break;
                }

                case ListChangeReason.AddRange:
                {
                    var wrappers = new List<TransformedValue<TSource, TDestination>>(change.Range.Count);
                    foreach (var slot in change.Range)
                    {
                        var wrapper = new TransformedValue<TSource, TDestination>(slot.Item, Optional<TDestination>.None);
                        _slotToValue[slot] = wrapper;
                        wrappers.Add(wrapper);
                    }

                    _pending.Add(new Change<TransformedValue<TSource, TDestination>>(ListChangeReason.AddRange, wrappers, change.Range.Index));

                    foreach (var slot in change.Range)
                    {
                        context.Track(slot, _transform(slot.Item));
                    }

                    break;
                }

                case ListChangeReason.Replace:
                {
                    var oldSlot = change.Item.Previous.Value;
                    var newSlot = change.Item.Current;
                    var hadOldWrapper = _slotToValue.TryGetValue(oldSlot, out var oldWrapper);

                    var newWrapper = new TransformedValue<TSource, TDestination>(newSlot.Item, Optional<TDestination>.None);
                    _slotToValue[newSlot] = newWrapper;
                    _slotToValue.Remove(oldSlot);

                    _pending.Add(new Change<TransformedValue<TSource, TDestination>>(
                        ListChangeReason.Replace,
                        newWrapper,
                        hadOldWrapper ? Optional.Some(oldWrapper) : Optional<TransformedValue<TSource, TDestination>>.None,
                        change.Item.CurrentIndex,
                        change.Item.PreviousIndex));

                    context.Untrack(oldSlot);
                    context.Track(newSlot, _transform(newSlot.Item));
                    break;
                }

                case ListChangeReason.Remove:
                {
                    var slot = change.Item.Current;
                    if (_slotToValue.TryGetValue(slot, out var wrapper))
                    {
                        _pending.Add(new Change<TransformedValue<TSource, TDestination>>(ListChangeReason.Remove, wrapper, change.Item.CurrentIndex));
                        _slotToValue.Remove(slot);
                    }

                    context.Untrack(slot);
                    break;
                }

                case ListChangeReason.RemoveRange:
                {
                    var wrappers = new List<TransformedValue<TSource, TDestination>>(change.Range.Count);
                    foreach (var slot in change.Range)
                    {
                        if (_slotToValue.TryGetValue(slot, out var wrapper))
                        {
                            wrappers.Add(wrapper);
                            _slotToValue.Remove(slot);
                        }

                        context.Untrack(slot);
                    }

                    _pending.Add(new Change<TransformedValue<TSource, TDestination>>(ListChangeReason.RemoveRange, wrappers, change.Range.Index));
                    break;
                }

                case ListChangeReason.Clear:
                {
                    var wrappers = new List<TransformedValue<TSource, TDestination>>(_slotToValue.Count);
                    foreach (var slot in change.Range)
                    {
                        if (_slotToValue.TryGetValue(slot, out var wrapper))
                        {
                            wrappers.Add(wrapper);
                        }

                        context.Untrack(slot);
                    }

                    _slotToValue.Clear();
                    _pending.Add(new Change<TransformedValue<TSource, TDestination>>(ListChangeReason.Clear, wrappers));
                    break;
                }

                case ListChangeReason.Refresh:
                {
                    var slot = change.Item.Current;
                    if (_slotToValue.TryGetValue(slot, out var wrapper))
                    {
                        _pending.Add(new Change<TransformedValue<TSource, TDestination>>(ListChangeReason.Refresh, wrapper, Optional<TransformedValue<TSource, TDestination>>.None, change.Item.CurrentIndex));
                    }

                    break;
                }

                case ListChangeReason.Moved:
                {
                    var slot = change.Item.Current;
                    if (_slotToValue.TryGetValue(slot, out var wrapper))
                    {
                        _pending.Add(new Change<TransformedValue<TSource, TDestination>>(wrapper, change.Item.CurrentIndex, change.Item.PreviousIndex));
                    }

                    break;
                }
            }
        }
    }

    public void OnInner(TDestination value, IListSlot<TSource> slot, IObserver<IChangeSet<TransformedValue<TSource, TDestination>>> emitter)
    {
        if (slot.IsReleased) return;
        if (!_slotToValue.TryGetValue(slot, out var existing)) return;

        var updated = new TransformedValue<TSource, TDestination>(slot.Item, Optional.Some(value));
        _slotToValue[slot] = updated;

        // Replace, not Refresh: the downstream Filter+Transform pipeline propagates Replace
        // as a value update, so Data reflects the new value. Refresh would emit a refresh
        // change that doesn't mutate Data.
        _pending.Add(new Change<TransformedValue<TSource, TDestination>>(
            ListChangeReason.Replace,
            updated,
            Optional.Some(existing),
            slot.CurrentIndex,
            slot.CurrentIndex));
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<TransformedValue<TSource, TDestination>>> emitter)
    {
        if (_pending.Count == 0) return;

        var snapshot = new ChangeSet<TransformedValue<TSource, TDestination>>(_pending);
        _pending.Clear();
        emitter.OnNext(snapshot);
    }
}
