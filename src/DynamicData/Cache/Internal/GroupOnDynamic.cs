// Copyright (c) 2011-2023 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Internal;
using DynamicData.Kernel;

namespace DynamicData.Cache.Internal;

internal sealed class GroupOnDynamic<TObject, TKey, TGroupKey>(IObservable<IChangeSet<TObject, TKey>> source, IObservable<Func<TObject, TKey, TGroupKey>> selectGroupObservable, IObservable<Unit>? regrouper = null)
    where TObject : notnull
    where TKey : notnull
    where TGroupKey : notnull
{
    public IObservable<IGroupChangeSet<TObject, TKey, TGroupKey>> Run() => Observable.Create<IGroupChangeSet<TObject, TKey, TGroupKey>>(observer =>
    {
        var dynamicGrouper = new Grouper();

        // Create shared observables for the 3 inputs
        var sharedSource = source.Synchronize(dynamicGrouper).Publish();
        var sharedGroupSelector = selectGroupObservable.DistinctUntilChanged().Synchronize(dynamicGrouper).Publish();
        var sharedRegrouper = (regrouper ?? Observable.Empty<Unit>()).Synchronize(dynamicGrouper).Publish();

        // Update the Group Selector
        var subGroupSelector = sharedGroupSelector
            .SubscribeSafe(onNext: groupSelector => dynamicGrouper.SetGroupSelector(groupSelector, observer), onError: observer.OnError);

        // Re-evaluate all the groupings each time it fires
        var subRegrouper = sharedRegrouper
            .SubscribeSafe(onNext: _ => dynamicGrouper.RegroupAll(observer), onError: observer.OnError);

        // Process the ChangeSet
        var subChanges = sharedSource
            .SubscribeSafe(onNext: changeSet => dynamicGrouper.ProcessChangeSet(changeSet, observer), onError: observer.OnError);

        // Create an observable that completes when all 3 inputs complete so the downstream can be completed as well
        var subOnComplete = Observable.Merge(sharedSource.ToUnit(), sharedGroupSelector.ToUnit(), sharedRegrouper)
            .SubscribeSafe(observer.OnError, observer.OnCompleted);

        return new CompositeDisposable(
            sharedGroupSelector.Connect(),
            sharedSource.Connect(),
            sharedRegrouper.Connect(),
            dynamicGrouper,
            subChanges,
            subGroupSelector,
            subRegrouper,
            subOnComplete);
    });

    private sealed class Grouper(Func<TObject, TKey, TGroupKey>? groupSelector = null) : GrouperBase<TObject, TKey, TGroupKey>, IDisposable
    {
        private readonly Cache<TObject, TKey> _pending = new();
        private Func<TObject, TKey, TGroupKey>? _groupSelector = groupSelector;

        public void ProcessChangeSet(IChangeSet<TObject, TKey> changeSet, IObserver<IGroupChangeSet<TObject, TKey, TGroupKey>> observer)
        {
            if (_groupSelector is null)
            {
                _pending.Clone(changeSet);
                return;
            }

            if (changeSet.Count == 0)
            {
                return;
            }

            var groupedChangeSet = new GroupChanges();

            foreach (var change in changeSet.ToConcreteType())
            {
                switch (change.Reason)
                {
                    case ChangeReason.Add:
                        {
                            var groupKey = _groupSelector(change.Current, change.Key);

                            groupedChangeSet.AddChange(groupKey, change);
                            SetGroupKey(groupKey, change.Key);
                        }

                        break;

                    case ChangeReason.Update:
                        {
                            var groupKey = _groupSelector(change.Current, change.Key);
                            var oldGroupKey = LookupGroupKey(change.Key);

                            if (oldGroupKey.HasValue)
                            {
                                // Key didn't change, so just forward the update change
                                if (KeyCompare(oldGroupKey.Value, groupKey))
                                {
                                    groupedChangeSet.AddChange(groupKey, change);
                                    continue;
                                }

                                // GroupKey changes, so convert to Add/Remove events instead of Update
                                groupedChangeSet.CreateAddChange(groupKey, change.Key, change.Current);
                                groupedChangeSet.CreateRemoveChange(oldGroupKey.Value, change.Key, change.Previous.Value);
                            }
                            else
                            {
                                Debug.Fail("Got Update for Key with Unknown Group Key");
                                groupedChangeSet.CreateAddChange(groupKey, change.Key, change.Current);
                            }

                            SetGroupKey(groupKey, change.Key);
                        }

                        break;

                    case ChangeReason.Remove:
                        {
                            var oldGroupKey = LookupGroupKey(change.Key);

                            if (oldGroupKey.HasValue)
                            {
                                // Forward the change
                                groupedChangeSet.AddChange(oldGroupKey.Value, change);
                                RemoveGroupKey(change.Key);
                            }
                            else
                            {
                                Debug.Fail("Got Remove for Key with unknown Group Key");
                                continue;
                            }
                        }

                        break;

                    case ChangeReason.Refresh:
                        {
                            var oldGroupKey = LookupGroupKey(change.Key);

                            if (!oldGroupKey.HasValue)
                            {
                                Debug.Fail("Got Refresh for Key with unknown Group Key");
                                continue;
                            }

                            var groupKey = _groupSelector(change.Current, change.Key);
                            var oldKey = oldGroupKey.Value;
                            if (KeyCompare(oldKey, groupKey))
                            {
                                // Forward the Refresh change
                                groupedChangeSet.AddChange(oldKey, change);
                                continue;
                            }

                            // GroupKey changed so convert to add/remove events
                            groupedChangeSet.CreateAddChange(groupKey, change.Key, change.Current);
                            groupedChangeSet.CreateRemoveChange(oldKey, change.Key, change.Current);
                            SetGroupKey(groupKey, change.Key);
                        }

                        break;
                }
            }

            PerformGroupChanges(groupedChangeSet);

            EmitChanges(observer);
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
            if (_groupSelector is not null)
            {
                _groupSelector = groupSelector;
                RegroupAll(observer);
            }
            else
            {
                _groupSelector = groupSelector;
                var groupChanges = new GroupChanges();
                foreach (var group in _pending.KeyValues.GroupBy(kvp => _groupSelector(kvp.Value, kvp.Key)))
                {
                    groupChanges.CreateAddChanges(group.Key, group);
                    UpdateGroupKeys(group);
                }

                _pending.Clear();
                PerformGroupChanges(groupChanges);
                EmitChanges(observer);
            }
        }
    }
}
