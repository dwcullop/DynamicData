// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;

using DynamicData.Cache.Internal;

namespace DynamicData.List.Internal;

/// <summary>
/// Orchestrator for the MergeManyCacheChangeSets shape (list source emitting child cache
/// changesets). Per source slot, holds a <see cref="ChangeSetCache{TObject,TKey}"/> wrapping
/// the child cache changeset. Shared <see cref="ChangeSetMergeTracker{TObject,TKey}"/>
/// merges all child changes; on slot removal, the slot's items are removed from the merged
/// cache. For Replace, RemoveItems for the old slot is deferred until after the new slot's
/// inner subscription has delivered its initial state, matching the legacy multicast ordering.
/// </summary>
internal sealed class MergeManyCacheChangeSetsOrchestrator<TObject, TDestination, TDestinationKey>
    : IListOrchestrator<TObject, IChangeSet<TDestination, TDestinationKey>, IChangeSet<TDestination, TDestinationKey>>
    where TObject : notnull
    where TDestination : notnull
    where TDestinationKey : notnull
{
    private readonly Func<TObject, IObservable<IChangeSet<TDestination, TDestinationKey>>> _selector;
    private readonly IEqualityComparer<TDestination>? _equalityComparer;
    private readonly IComparer<TDestination>? _comparer;
    private readonly List<ChangeSetCache<TDestination, TDestinationKey>> _caches = new();
    private readonly Dictionary<IListSlot<TObject>, ChangeSetCache<TDestination, TDestinationKey>> _slotToCache = new();
    private readonly ChangeSetMergeTracker<TDestination, TDestinationKey> _tracker;
    private bool _pendingChanges;

    public MergeManyCacheChangeSetsOrchestrator(
        Func<TObject, IObservable<IChangeSet<TDestination, TDestinationKey>>> selector,
        IEqualityComparer<TDestination>? equalityComparer,
        IComparer<TDestination>? comparer)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        _equalityComparer = equalityComparer;
        _comparer = comparer;
        _tracker = new ChangeSetMergeTracker<TDestination, TDestinationKey>(() => _caches, _comparer, _equalityComparer);
    }

    public void OnSourceChangeSet(IChangeSet<IListSlot<TObject>> changes, IListOrchestratorContext<TObject, IChangeSet<TDestination, TDestinationKey>> context)
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
                    // Order matters: subscribe the new slot FIRST so its initial state
                    // delivers (synchronously, via the queue's reentrant drain) before we
                    // defer the RemoveItems for the old slot.
                    TrackSlot(change.Item.Current, context);
                    if (change.Item.Previous.HasValue)
                    {
                        ReplaceOldSlot(change.Item.Previous.Value, context);
                    }

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

    public void OnInner(IChangeSet<TDestination, TDestinationKey> value, IListSlot<TObject> slot, IObserver<IChangeSet<TDestination, TDestinationKey>> emitter)
    {
        _tracker.ProcessChangeSet(value);
        _pendingChanges = true;
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<TDestination, TDestinationKey>> emitter)
    {
        if (!_pendingChanges) return;

        _pendingChanges = false;
        _tracker.EmitChanges(emitter);
    }

    private void TrackSlot(IListSlot<TObject> slot, IListOrchestratorContext<TObject, IChangeSet<TDestination, TDestinationKey>> context)
    {
        var cache = new ChangeSetCache<TDestination, TDestinationKey>(_selector(slot.Item));
        _slotToCache[slot] = cache;
        _caches.Add(cache);
        context.Track(slot, cache.Source);
    }

    private void UntrackSlot(IListSlot<TObject> slot, IListOrchestratorContext<TObject, IChangeSet<TDestination, TDestinationKey>> context)
    {
        context.Untrack(slot);

        if (_slotToCache.TryGetValue(slot, out var cache))
        {
            _caches.Remove(cache);
            _slotToCache.Remove(slot);
            _tracker.RemoveItems(cache.Cache.KeyValues);
            _pendingChanges = true;
        }
    }

    /// <summary>
    /// Replace tear-down: detach the old slot's subscription, then defer the RemoveItems
    /// call so it runs after the new slot's inner subscription has delivered its initial
    /// state. This matches the legacy implementation's Publish multicast ordering where
    /// MergeMany's per-item subscription replacement runs before OnItemRemoved's
    /// RemoveItems, so the call becomes a no-op for keys the new slot also publishes and
    /// only removes non-shared keys.
    /// </summary>
    private void ReplaceOldSlot(IListSlot<TObject> slot, IListOrchestratorContext<TObject, IChangeSet<TDestination, TDestinationKey>> context)
    {
        context.Untrack(slot);

        if (_slotToCache.TryGetValue(slot, out var cache))
        {
            _caches.Remove(cache);
            _slotToCache.Remove(slot);

            context.DeferAction(() =>
            {
                _tracker.RemoveItems(cache.Cache.KeyValues);
                _pendingChanges = true;
            });
        }
    }
}
