// Copyright (c) 2011-2023 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using DynamicData.Kernel;

namespace DynamicData.Cache.Internal;

internal sealed class DynamicGrouper<TObject, TKey, TGroupKey>(Func<TObject, TKey, TGroupKey>? groupSelector = null) : GrouperBase<TObject, TKey, TGroupKey>, IDisposable
    where TObject : notnull
    where TKey : notnull
    where TGroupKey : notnull
{
    private Func<TObject, TKey, TGroupKey>? _groupSelector = groupSelector;

    public bool HasGroupSelector => _groupSelector is not null;

    public void AddOrUpdate(TKey key, TGroupKey groupKey, TObject item, IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>>? observer = null)
    {
        PerformAddOrUpdate(key, groupKey, item);

        if (observer != null)
        {
            EmitChanges(observer);
        }
    }

    public void ProcessChangeSet(IChangeSet<TObject, TKey> changeSet, IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>>? observer = null)
    {
        foreach (var change in changeSet.ToConcreteType())
        {
            switch (change.Reason)
            {
                case ChangeReason.Add when _groupSelector is not null:
                    PerformAddOrUpdate(change.Key, _groupSelector(change.Current, change.Key), change.Current);
                    break;

                case ChangeReason.Remove:
                    PerformRemove(change.Key);
                    break;

                case ChangeReason.Update when _groupSelector is not null:
                    PerformAddOrUpdate(change.Key, _groupSelector(change.Current, change.Key), change.Current);
                    break;

                case ChangeReason.Update:
                    PerformUpdate(change.Key);
                    break;

                case ChangeReason.Refresh when _groupSelector is not null:
                    PerformRefresh(change.Key, _groupSelector(change.Current, change.Key), change.Current);
                    break;

                case ChangeReason.Refresh:
                    PerformRefresh(change.Key);
                    break;
            }
        }

        if (observer != null)
        {
            EmitChanges(observer);
        }
    }

    // Re-evaluate the GroupSelector for each item and apply the changes so that each group only emits a single changset
    // Perform all the adds/removes for each group in a single step
    public void RegroupAll(IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>> observer)
    {
        if (_groupSelector == null)
        {
            Debug.Fail("RegroupAll called without a GroupSelector. No changes will be made.");
            return;
        }

        // Create an array of tuples with data for items whose GroupKeys have changed
        var groupChanges = GetGroups().Select(static group => group as ManagedGroup<TObject, TKey, TGroupKey>)
            .SelectMany(group => group!.Cache.KeyValues.Select(
                kvp => (KeyValuePair: kvp, OldGroup: group, NewGroupKey: _groupSelector(kvp.Value, kvp.Key))))
            .Where(static x => !EqualityComparer<TGroupKey>.Default.Equals(x.OldGroup.Key, x.NewGroupKey))
            .ToArray();

        // Build a list of the removals that need to happen (grouped by the old key)
        var pendingRemoves = groupChanges
            .GroupBy(
                static x => x.OldGroup.Key,
                static x => (x.KeyValuePair.Key, x.OldGroup))
            .ToDictionary(g => g.Key, g => g.AsEnumerable());

        // Build a list of the adds that need to happen (grouped by the new key)
        var pendingAddList = groupChanges
            .GroupBy(
                static x => x.NewGroupKey,
                static x => x.KeyValuePair)
            .ToList();

        // Iterate the list of groups that need something added (also maybe removed)
        foreach (var add in pendingAddList)
        {
            // Get a list of keys to be removed from this group (if any)
            var removeKeyList =
                pendingRemoves.TryGetValue(add.Key, out var removes)
                    ? removes.Select(static r => r.Key)
                    : Enumerable.Empty<TKey>();

            // Obtained the ManagedGroup instance and perform all of the pending updates at once
            var newGroup = GetOrAddGroup(add.Key);
            newGroup.Update(updater =>
            {
                updater.RemoveKeys(removeKeyList);
                updater.AddOrUpdate(add);
            });

            // Update the key cache
            UpdateGroupKeys(add);

            // Remove from the pendingRemove dictionary because these removes have been handled
            pendingRemoves.Remove(add.Key);
        }

        // Everything left in the Dictionary represents a group that had items removed but no items added
        foreach (var removeList in pendingRemoves.Values)
        {
            var group = removeList.First().OldGroup;
            group.Update(updater => updater.RemoveKeys(removeList.Select(static kvp => kvp.Key)));

            CheckEmptyGroup(group);
        }

        EmitChanges(observer);
    }

    public void SetGroupSelector(Func<TObject, TKey, TGroupKey> groupSelector, IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>> observer)
    {
        _groupSelector = groupSelector;
        RegroupAll(observer);
    }

    public void Initialize(IEnumerable<KeyValuePair<TKey, TObject>> initialValues, Func<TObject, TKey, TGroupKey> groupSelector, IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>> observer)
    {
        if (_groupSelector != null)
        {
            Debug.Fail("Initialize called when a GroupSelector is already present. No changes will be made.");
            return;
        }

        _groupSelector = groupSelector;
        foreach (var kvp in initialValues)
        {
            PerformAddOrUpdate(kvp.Key, _groupSelector(kvp.Value, kvp.Key), kvp.Value);
        }

        EmitChanges(observer);
    }
}
