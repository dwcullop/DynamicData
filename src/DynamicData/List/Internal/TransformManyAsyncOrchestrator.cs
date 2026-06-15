// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;

namespace DynamicData.List.Internal;

/// <summary>
/// Orchestrator for TransformManyAsync: per source slot, await an async transformer that
/// produces a child list changeset; merge all child changesets into a single output.
/// </summary>
internal sealed class TransformManyAsyncOrchestrator<TSource, TDestination>
    : IListOrchestrator<TSource, IChangeSet<TDestination>, IChangeSet<TDestination>>
    where TSource : notnull
    where TDestination : notnull
{
    private readonly Func<TSource, Task<IObservable<IChangeSet<TDestination>>>> _transformer;
    private readonly IEqualityComparer<TDestination>? _equalityComparer;
    private readonly ChangeSetMergeTracker<TDestination> _tracker = new();

    // Per-slot list state, mutated only inside OnInner (drain thread). Previously this was
    // delegated to ClonedListChangeSet.Source which applied .Do(List.Clone) to the raw async
    // child observable BEFORE the orchestrator serialized it through the SDQ. List mutation
    // then ran on whatever thread the inner emission arrived on, while UntrackSlot read the
    // same list from the drain thread - a real race. Owning the list state here moves all
    // mutation to the drain thread.
    private readonly Dictionary<IListSlot<TSource>, ChangeAwareList<TDestination>> _slotLists = new();

    private bool _pendingChanges;

    public TransformManyAsyncOrchestrator(
        Func<TSource, Task<IObservable<IChangeSet<TDestination>>>> transformer,
        IEqualityComparer<TDestination>? equalityComparer)
    {
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
        _equalityComparer = equalityComparer;
    }

    public void OnSourceChangeSet(IChangeSet<IListSlot<TSource>> changes, IListOrchestratorContext<TSource, IChangeSet<TDestination>> context)
    {
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ListChangeReason.Add:
                    TrackSlot(change.Item.Current, context);
                    break;
                case ListChangeReason.AddRange:
                    foreach (var slot in change.Range)
                    {
                        TrackSlot(slot, context);
                    }

                    break;
                case ListChangeReason.Replace:
                    if (change.Item.Previous.HasValue)
                    {
                        UntrackSlot(change.Item.Previous.Value, context);
                    }

                    TrackSlot(change.Item.Current, context);
                    break;
                case ListChangeReason.Remove:
                    UntrackSlot(change.Item.Current, context);
                    break;
                case ListChangeReason.RemoveRange:
                case ListChangeReason.Clear:
                    foreach (var slot in change.Range)
                    {
                        UntrackSlot(slot, context);
                    }

                    break;
            }
        }
    }

    public void OnInner(IChangeSet<TDestination> value, IListSlot<TSource> slot, IObserver<IChangeSet<TDestination>> emitter)
    {
        // Guard against zombie emissions arriving on the drain thread after the slot has
        // been untracked (concurrent source-Remove with an in-flight inner emission).
        if (slot.IsReleased) return;

        if (!_slotLists.TryGetValue(slot, out var list))
        {
            list = new ChangeAwareList<TDestination>();
            _slotLists[slot] = list;
        }

        if (_equalityComparer is null)
        {
            list.Clone(value);
        }
        else
        {
            list.Clone(value, _equalityComparer);
        }

        _tracker.ProcessChangeSet(value);
        _pendingChanges = true;
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<TDestination>> emitter)
    {
        if (!_pendingChanges) return;

        _pendingChanges = false;
        _tracker.EmitChanges(emitter);
    }

    private void TrackSlot(IListSlot<TSource> slot, IListOrchestratorContext<TSource, IChangeSet<TDestination>> context)
    {
        // The transformer returns a Task<IObservable>; FromAsync awaits the task on subscribe,
        // then SelectMany unwraps the returned observable to deliver its emissions to OnInner.
        var asyncChild = Observable.FromAsync(() => _transformer(slot.Item)).SelectMany(obs => obs);
        context.Track(slot, asyncChild);
    }

    private void UntrackSlot(IListSlot<TSource> slot, IListOrchestratorContext<TSource, IChangeSet<TDestination>> context)
    {
        context.Untrack(slot);

        if (_slotLists.TryGetValue(slot, out var list))
        {
            _tracker.RemoveItems(list);
            _slotLists.Remove(slot);
            _pendingChanges = true;
        }
    }
}
