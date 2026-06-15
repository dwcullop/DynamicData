// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Orchestrator for GroupOnObservable: per source slot, track a group-key observable. When
/// the observable emits, the source item moves into the indicated group; groups are created
/// on first use and removed when empty. Output is a list changeset of <see cref="IGroup{TObject,
/// TGroupKey}"/> values.
/// </summary>
internal sealed class GroupOnObservableOrchestrator<TObject, TGroupKey>
    : IListOrchestrator<TObject, TGroupKey, IChangeSet<IGroup<TObject, TGroupKey>>>, IDisposable
    where TObject : notnull
    where TGroupKey : notnull
{
    private readonly Func<TObject, IObservable<TGroupKey>> _groupKeySelector;
    private readonly Dictionary<TGroupKey, Group<TObject, TGroupKey>> _groupsByKey = new();
    private readonly Dictionary<IListSlot<TObject>, TGroupKey> _slotToGroupKey = new();
    private readonly ChangeAwareList<IGroup<TObject, TGroupKey>> _groupList = new();

    public GroupOnObservableOrchestrator(Func<TObject, IObservable<TGroupKey>> groupKeySelector) =>
        _groupKeySelector = groupKeySelector ?? throw new ArgumentNullException(nameof(groupKeySelector));

    public void OnSourceChangeSet(IChangeSet<IListSlot<TObject>> changes, IListOrchestratorContext<TObject, TGroupKey> context)
    {
        foreach (var change in changes)
        {
            switch (change.Reason)
            {
                case ListChangeReason.Add:
                    StartTracking(change.Item.Current, context);
                    break;

                case ListChangeReason.AddRange:
                    foreach (var slot in change.Range)
                    {
                        StartTracking(slot, context);
                    }

                    break;

                case ListChangeReason.Replace:
                    if (change.Item.Previous.HasValue)
                    {
                        StopTracking(change.Item.Previous.Value, context);
                    }

                    StartTracking(change.Item.Current, context);
                    break;

                case ListChangeReason.Remove:
                    StopTracking(change.Item.Current, context);
                    break;

                case ListChangeReason.RemoveRange:
                case ListChangeReason.Clear:
                    foreach (var slot in change.Range)
                    {
                        StopTracking(slot, context);
                    }

                    break;

                case ListChangeReason.Refresh:
                {
                    // Propagate the refresh to the group containing this slot's item. Matches
                    // the cache analog's behaviour where source Refresh is forwarded to the
                    // group's internal list so downstream consumers see the property change.
                    var slot = change.Item.Current;
                    if (_slotToGroupKey.TryGetValue(slot, out var key) &&
                        _groupsByKey.TryGetValue(key, out var group))
                    {
                        group.Edit(list =>
                        {
                            if (list is ChangeAwareList<TObject> awareList)
                            {
                                awareList.Refresh(slot.Item);
                            }
                        });
                    }

                    break;
                }
            }
        }
    }

    public void OnInner(TGroupKey value, IListSlot<TObject> slot, IObserver<IChangeSet<IGroup<TObject, TGroupKey>>> emitter)
    {
        if (slot.IsReleased) return;

        var hadPriorKey = _slotToGroupKey.TryGetValue(slot, out var priorKey);

        if (hadPriorKey && EqualityComparer<TGroupKey>.Default.Equals(priorKey!, value))
        {
            return;
        }

        if (hadPriorKey)
        {
            RemoveFromGroup(slot.Item, priorKey!);
        }

        AddToGroup(slot.Item, value);
        _slotToGroupKey[slot] = value;
    }

    public void OnDrainComplete(bool isFinal, IObserver<IChangeSet<IGroup<TObject, TGroupKey>>> emitter)
    {
        var groupChanges = _groupList.CaptureChanges();
        if (groupChanges.Count > 0)
        {
            emitter.OnNext(groupChanges);
        }
    }

    /// <summary>
    /// Dispose all live groups when the orchestration is torn down. Without this, groups
    /// remaining at completion/dispose time keep their internal SourceList&lt;T&gt; instances
    /// alive (and their internal Subject subscribers), which masks bugs and leaks resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var group in _groupsByKey.Values)
        {
            group.Dispose();
        }

        _groupsByKey.Clear();
        _slotToGroupKey.Clear();
    }

    private void StartTracking(IListSlot<TObject> slot, IListOrchestratorContext<TObject, TGroupKey> context) =>
        context.Track(slot, _groupKeySelector(slot.Item));

    private void StopTracking(IListSlot<TObject> slot, IListOrchestratorContext<TObject, TGroupKey> context)
    {
        context.Untrack(slot);

        if (_slotToGroupKey.TryGetValue(slot, out var key))
        {
            RemoveFromGroup(slot.Item, key);
            _slotToGroupKey.Remove(slot);
        }
    }

    private void AddToGroup(TObject item, TGroupKey key)
    {
        if (!_groupsByKey.TryGetValue(key, out var group))
        {
            group = new Group<TObject, TGroupKey>(key);
            _groupsByKey[key] = group;
            _groupList.Add(group);
        }

        group.Edit(list => list.Add(item));
    }

    private void RemoveFromGroup(TObject item, TGroupKey key)
    {
        if (!_groupsByKey.TryGetValue(key, out var group)) return;

        group.Edit(list => list.Remove(item));

        if (group.List.Count == 0)
        {
            _groupsByKey.Remove(key);
            _groupList.Remove(group);
            group.Dispose();
        }
    }
}
