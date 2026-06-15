// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;

namespace DynamicData.List.Internal;

/// <summary>
/// Orchestrator for the MergeManyListChangeSets shape: per source slot, subscribe to a child
/// list changeset; merge all child changes into a single output via a <see cref="ChangeSetMergeTracker{TObject}"/>.
/// When a source slot is removed, remove all items that slot's child stream had contributed.
/// </summary>
internal sealed class MergeManyListChangeSetsOrchestrator<TObject, TDestination>
    : IListOrchestrator<TObject, IChangeSet<TDestination>, IChangeSet<TDestination>>
    where TObject : notnull
    where TDestination : notnull
{
    private readonly Func<TObject, IObservable<IChangeSet<TDestination>>> _selector;
    private readonly IEqualityComparer<TDestination>? _equalityComparer;
    private readonly ChangeSetMergeTracker<TDestination> _tracker = new();
    private readonly Dictionary<IListSlot<TObject>, ClonedListChangeSet<TDestination>> _clones = new();
    private bool _pendingChanges;

    public MergeManyListChangeSetsOrchestrator(
        Func<TObject, IObservable<IChangeSet<TDestination>>> selector,
        IEqualityComparer<TDestination>? equalityComparer)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _equalityComparer = equalityComparer;
    }

    public void OnSourceChangeSet(IChangeSet<IListSlot<TObject>> changes, IListOrchestratorContext<TObject, IChangeSet<TDestination>> context)
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

    public void OnInner(IChangeSet<TDestination> value, IListSlot<TObject> slot, IObserver<IChangeSet<TDestination>> emitter)
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

    private void TrackSlot(IListSlot<TObject> slot, IListOrchestratorContext<TObject, IChangeSet<TDestination>> context)
    {
        var clone = new ClonedListChangeSet<TDestination>(_selector(slot.Item), _equalityComparer);
        _clones[slot] = clone;
        context.Track(slot, clone.Source);
    }

    private void UntrackSlot(IListSlot<TObject> slot, IListOrchestratorContext<TObject, IChangeSet<TDestination>> context)
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
