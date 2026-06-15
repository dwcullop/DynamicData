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
    private readonly Dictionary<IListSlot<TSource>, ClonedListChangeSet<TDestination>> _clones = new();
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
        var clone = new ClonedListChangeSet<TDestination>(asyncChild, _equalityComparer);
        _clones[slot] = clone;
        context.Track(slot, clone.Source);
    }

    private void UntrackSlot(IListSlot<TSource> slot, IListOrchestratorContext<TSource, IChangeSet<TDestination>> context)
    {
        context.Untrack(slot);

        if (_clones.TryGetValue(slot, out var clone))
        {
            _tracker.RemoveItems(clone.List);
            _clones.Remove(slot);
            _pendingChanges = true;
        }
    }
}
