// Copyright (c) 2011-2023 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections;
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

    public void Dispose() => _groupCache.Items.ForEach(group => (group as ManagedGroup<TObject, TKey, TGroupKey>)?.Dispose());

    public void EmitChanges(IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>> observer)
    {
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

    protected static void PerformGroupRefresh(TKey key, in Optional<ManagedGroup<TObject, TKey, TGroupKey>> optionalGroup)
    {
        if (optionalGroup.HasValue)
        {
            optionalGroup.Value.Update(updater => updater.Refresh(key));
        }
        else
        {
            Debug.Fail("Should not receive a refresh for an unknown Group Key");
        }
    }

    protected IEnumerable<IGroup<TObject, TKey, TGroupKey>> GetGroups() => _groupCache.Items;

    protected void CheckEmptyGroup(ManagedGroup<TObject, TKey, TGroupKey> group)
    {
        // If it is now empty, flag it for cleanup
        if (group.Count == 0)
        {
            _emptyGroups.Add(group);
        }
    }

    protected void UpdateGroupKeys(IGrouping<TGroupKey, KeyValuePair<TKey, TObject>> add)
    {
        foreach (var kvp in add)
        {
            _groupKeys[kvp.Key] = add.Key;
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

    protected void PerformAddOrUpdate(TKey key, TGroupKey groupKey, TObject item)
    {
        // See if this item already has been grouped
        if (_groupKeys.TryGetValue(key, out var currentGroupKey))
        {
            // See if the key has changed
            if (EqualityComparer<TGroupKey>.Default.Equals(groupKey, currentGroupKey))
            {
                // GroupKey did not change, so just update the value in the group
                var optionalGroup = LookupGroup(currentGroupKey);
                if (optionalGroup.HasValue)
                {
                    optionalGroup.Value.Update(updater => updater.AddOrUpdate(item, key));
                    return;
                }

                Debug.Fail("If there is a GroupKey associated with a Key, the Group for that GroupKey should exist.");
            }
            else
            {
                // GroupKey changed, so remove from old and allow to be added below
                PerformRemove(key, currentGroupKey);
            }
        }

        // Find the right group and add the item
        PerformGroupAddOrUpdate(key, groupKey, item);
    }

    protected void PerformAddOrUpdates(IEnumerable<IGrouping<TGroupKey, KeyValuePair<TKey, TObject>>> updates)
    {
        foreach (var update in updates)
        {
            GetOrAddGroup(update.Key).Update(updater => updater.AddOrUpdate(update));
            UpdateGroupKeys(update);
        }
    }

    protected void PerformGroupAddOrUpdate(TKey key, TGroupKey groupKey, TObject item)
    {
        var group = GetOrAddGroup(groupKey);
        group.Update(updater => updater.AddOrUpdate(item, key));
        _groupKeys[key] = groupKey;

        // Can't be empty since a value was just added
        _emptyGroups.Remove(group);
    }

    protected void PerformRefresh(TKey key) => PerformGroupRefresh(key, LookupGroup(key));

    // When the GroupKey is available, check then and move the group if it changed
    protected void PerformRefresh(TKey key, TGroupKey newGroupKey, TObject item)
    {
        if (_groupKeys.TryGetValue(key, out var groupKey))
        {
            // See if the key has changed
            if (EqualityComparer<TGroupKey>.Default.Equals(newGroupKey, groupKey))
            {
                // GroupKey did not change, so just refresh the value in the group
                PerformGroupRefresh(key, LookupGroup(groupKey));
            }
            else
            {
                // GroupKey changed, so remove from old and add to new
                PerformRemove(key, groupKey);
                PerformGroupAddOrUpdate(key, newGroupKey, item);
            }
        }
        else
        {
            Debug.Fail("Should not receive a refresh for an unknown key");
        }
    }

    protected void PerformRemove(TKey key)
    {
        if (_groupKeys.TryGetValue(key, out var groupKey))
        {
            PerformRemove(key, groupKey);
            _groupKeys.Remove(key);
        }
        else
        {
            Debug.Fail("Should not receive a Remove Event for an unknown key");
        }
    }

    protected void PerformRemove(TKey key, TGroupKey groupKey)
    {
        var optionalGroup = LookupGroup(groupKey);
        if (optionalGroup.HasValue)
        {
            var currentGroup = optionalGroup.Value;
            currentGroup.Update(updater =>
            {
                updater.Remove(key);
                if (updater.Count == 0)
                {
                    _emptyGroups.Add(currentGroup);
                }
            });
        }
        else
        {
            Debug.Fail("Should not receive a Remove Event for an unknown Group Key");
        }
    }

    protected void PerformGroupChanges(IEnumerable<GroupChanges> groupChanges)
    {
        foreach (var groupChange in groupChanges)
        {
            if (groupChange.AddsOrUpdates.Count > 0)
            {
                var group = GetOrAddGroup(groupChange.GroupKey);
                group.Update(updater =>
                {
                    updater.AddOrUpdate(groupChange.AddsOrUpdates);
                    updater.RemoveKeys(groupChange.Removes);
                    updater.Refresh(groupChange.Refreshes);
                });
            }
            else if (groupChange.Removes.Count > 0 || groupChange.Removes.Count > 0)
            {
                var optionalGroup = LookupGroup(groupChange.GroupKey);
                if (optionalGroup.HasValue)
                {
                    var group = optionalGroup.Value;
                    group.Update(updater =>
                    {
                        updater.RemoveKeys(groupChange.Removes);
                        updater.Refresh(groupChange.Refreshes);
                        if (updater.Count == 0)
                        {
                            _emptyGroups.Add(group);
                        }
                    });
                }
                else
                {
                    Debug.Fail("Should not receive Events for an unknown Group Key");
                }
            }
        }
    }

    protected class GroupChangeSet
    {
        private readonly Dictionary<TGroupKey, GroupChanges> _groupChanges = [];

        public IEnumerable<GroupChanges> GroupChanges => _groupChanges.Values;

        public IEnumerable<TGroupKey> GroupKeys => _groupChanges.Keys;

        public void AddOrUpdate(TGroupKey groupKey, TKey key, TObject item) => AddOrUpdate(groupKey, new KeyValuePair<TKey, TObject>(key, item));

        public void AddOrUpdate(TGroupKey groupKey, KeyValuePair<TKey, TObject> kvp) => GetGroupChanges(groupKey).AddsOrUpdates.Add(kvp);

        public void Remove(TGroupKey groupKey, TKey key) => GetGroupChanges(groupKey).Removes.Add(key);

        public void Refresh(TGroupKey groupKey, TKey key) => GetGroupChanges(groupKey).Refreshes.Add(key);

        private GrouperBase<TObject, TKey, TGroupKey>.GroupChanges GetGroupChanges(TGroupKey groupKey)
        {
            if (!_groupChanges.TryGetValue(groupKey, out var changes))
            {
                _groupChanges[groupKey] = changes = new GroupChanges(groupKey);
            }

            return changes;
        }
    }

    protected class GroupChanges(TGroupKey groupKey, IEnumerable<KeyValuePair<TKey, TObject>>? adds = null, IEnumerable<TKey>? removes = null, IEnumerable<TKey>? refreshes = null)
    {
        public GroupChanges(IGrouping<TGroupKey, KeyValuePair<TKey, TObject>> updates)
            : this(updates.Key, updates, null)
        {
        }

        public TGroupKey GroupKey { get; } = groupKey;

        public List<KeyValuePair<TKey, TObject>> AddsOrUpdates { get; } = adds?.ToList() ?? [];

        public List<TKey> Removes { get; } = removes?.ToList() ?? [];

        public List<TKey> Refreshes { get; } = refreshes?.ToList() ?? [];
    }
}
