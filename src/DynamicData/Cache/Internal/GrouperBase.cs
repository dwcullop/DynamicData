// Copyright (c) 2011-2023 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using DynamicData.Kernel;

namespace DynamicData.Cache.Internal;

internal class GrouperBase<TObject, TKey, TGroupKey>
    where TObject : notnull
    where TKey : notnull
    where TGroupKey : notnull
{
    private readonly HashSet<ManagedGroup<TObject, TKey, TGroupKey>> _emptyGroups = [];
    private readonly ChangeAwareCache<IGroup<TObject, TKey, TGroupKey>, TGroupKey> _groupCache = new();
    private readonly Dictionary<TKey, TGroupKey> _groupKeys = [];

    protected GroupChanges PendingChanges { get; } = new();

    public void Dispose() => _groupCache.Items.ForEach(group => (group as ManagedGroup<TObject, TKey, TGroupKey>)?.Dispose());

    public void EmitChanges(IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>> observer)
    {
        PerformGroupChanges(PendingChanges);
        PendingChanges.Clear();

        // Verify logic doesn't capture any non-empty groups
        Debug.Assert(_emptyGroups.All(static group => group.Cache.Count == 0), "Non empty Group in Empty Group HashSet");

        // Dispose/Remove any empty groups
        foreach (var group in _emptyGroups)
        {
            if (group.Count == 0)
            {
                _groupCache.Remove(group.Key);
                group.Dispose();
            }
        }

        _emptyGroups.Clear();

        // Make sure no empty ones were missed
        Debug.Assert(!_groupCache.Items.Any(static group => group.Cache.Count == 0), "Not all empty Groups were removed");

        // Emit any pending changes
        var changeSet = _groupCache.CaptureChanges();
        if (changeSet.Count != 0)
        {
            observer.OnNext(new GroupChangeSet<TObject, TKey, TGroupKey>(changeSet));
        }
    }

    protected static bool KeyCompare(TGroupKey a, TGroupKey b) => EqualityComparer<TGroupKey>.Default.Equals(a, b);

    protected IEnumerable<IGroup<TObject, TKey, TGroupKey>> GetGroups() => _groupCache.Items;

    protected void CheckEmptyGroup(ManagedGroup<TObject, TKey, TGroupKey> group)
    {
        // If it is now empty, flag it for cleanup
        if (group.Count == 0)
        {
            _emptyGroups.Add(group);
        }
    }

    protected void UpdateGroupKeys(IGrouping<TGroupKey, KeyValuePair<TKey, TObject>> grouping)
    {
        foreach (var kvp in grouping)
        {
            _groupKeys[kvp.Key] = grouping.Key;
        }
    }

    protected ManagedGroup<TObject, TKey, TGroupKey> GetOrAddGroup(TGroupKey groupKey) =>
        LookupGroup(groupKey).ValueOr(() =>
        {
            var newGroup = new ManagedGroup<TObject, TKey, TGroupKey>(groupKey);
            _groupCache.Add(newGroup, groupKey);
            return newGroup;
        });

    protected Optional<ManagedGroup<TObject, TKey, TGroupKey>> LookupGroup(TGroupKey groupKey) =>
        _groupCache.Lookup(groupKey).Convert(static grp => (grp as ManagedGroup<TObject, TKey, TGroupKey>)!);

    protected Optional<ManagedGroup<TObject, TKey, TGroupKey>> LookupGroup(TKey key) =>
        _groupKeys.Lookup(key).Convert(LookupGroup);

    protected Optional<TGroupKey> LookupGroupKey(TKey key) => _groupKeys.Lookup(key);

    protected void SetGroupKey(TGroupKey groupKey, TKey key) => _groupKeys[key] = groupKey;

    protected void RemoveGroupKey(TKey key) => _groupKeys.Remove(key);

    protected void CreateAddOrUpdateChanges(TKey key, TGroupKey groupKey, TObject item)
    {
        // See if this item already has been grouped
        if (_groupKeys.TryGetValue(key, out var currentGroupKey))
        {
            // See if the key has changed
            if (!KeyCompare(groupKey, currentGroupKey))
            {
                PendingChanges.CreateAddChange(groupKey, key, item);
                PendingChanges.CreateRemoveChange(currentGroupKey, key, item);
                _groupKeys[key] = groupKey;
            }
        }
        else
        {
            PendingChanges.CreateAddChange(groupKey, key, item);
            _groupKeys[key] = groupKey;
        }
    }

    protected void CreateRemoveChange(TKey key, TObject item)
    {
        if (_groupKeys.TryGetValue(key, out var groupKey))
        {
            PendingChanges.CreateRemoveChange(groupKey, key, item);
            _groupKeys.Remove(key);
        }
    }

    protected void CreateRefreshChange(TKey key, TObject item)
    {
        if (_groupKeys.TryGetValue(key, out var groupKey))
        {
            PendingChanges.CreateRefreshChange(groupKey, key, item);
        }
    }

    protected void PerformGroupChanges(GroupChanges groupChanges)
    {
        foreach (var groupChange in groupChanges.Changes)
        {
            var group = GetOrAddGroup(groupChange.Key);
            group.Update(updater =>
            {
                updater.Clone(new ChangeSet<TObject, TKey>(groupChange.Value));
                _ = (updater.Count != 0) ? _emptyGroups.Remove(group) : _emptyGroups.Add(group);
            });
        }
    }

    protected class GroupChanges
    {
        private readonly Dictionary<TGroupKey, List<Change<TObject, TKey>>> _groupChanges = [];

        public IEnumerable<KeyValuePair<TGroupKey, List<Change<TObject, TKey>>>> Changes => _groupChanges;

        public void AddChange(TGroupKey groupKey, in Change<TObject, TKey> change) => GetGroupChanges(groupKey).Add(change);

        public void AddChanges(TGroupKey groupKey, IEnumerable<Change<TObject, TKey>> changes) => GetGroupChanges(groupKey).Add(changes);

        public void Clear() => _groupChanges.Clear();

        public void CreateAddChange(TGroupKey groupKey, TKey key, TObject item) => AddChange(groupKey, new Change<TObject, TKey>(ChangeReason.Add, key, item));

        public void CreateAddChanges(TGroupKey groupKey, IEnumerable<KeyValuePair<TKey, TObject>> kvps) => AddChanges(groupKey, kvps.Select(kvp => new Change<TObject, TKey>(ChangeReason.Add, kvp.Key, kvp.Value)));

        public void CreateUpdateChange(TGroupKey groupKey, TKey key, TObject item, TObject prev) => AddChange(groupKey, new Change<TObject, TKey>(ChangeReason.Update, key, item, Optional.Some(prev)));

        public void CreateRemoveChange(TGroupKey groupKey, TKey key, TObject item) => AddChange(groupKey, new Change<TObject, TKey>(ChangeReason.Remove, key, item));

        public void CreateRefreshChange(TGroupKey groupKey, TKey key, TObject item) => AddChange(groupKey, new Change<TObject, TKey>(ChangeReason.Refresh, key, item));

        private List<Change<TObject, TKey>> GetGroupChanges(TGroupKey groupKey)
        {
            if (!_groupChanges.TryGetValue(groupKey, out var changes))
            {
                _groupChanges[groupKey] = changes = [];
            }

            return changes;
        }
    }
}
