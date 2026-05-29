// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;
using System.Reactive.Linq;

using DynamicData.Internal;

namespace DynamicData.Cache.Internal;

internal sealed class AutoRefresh<TObject, TKey, TAny>(IObservable<IChangeSet<TObject, TKey>> source, Func<TObject, TKey, IObservable<TAny>> reEvaluator, TimeSpan? buffer = null, IScheduler? scheduler = null)
    where TObject : notnull
    where TKey : notnull
{
    private readonly Func<TObject, TKey, IObservable<TAny>> _reEvaluator = reEvaluator ?? throw new ArgumentNullException(nameof(reEvaluator));

    private readonly IScheduler _scheduler = scheduler ?? GlobalConfig.DefaultScheduler;

    private readonly IObservable<IChangeSet<TObject, TKey>> _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<IChangeSet<TObject, TKey>> Run() => Observable.Defer(() =>
    {
        var cache = new ChangeAwareCache<TObject, TKey>();

        var changes = _source.AggregateMany<TObject, TKey, TAny, IChangeSet<TObject, TKey>>(
            onParent: (parentChanges, setChild) =>
            {
                cache.Clone(parentChanges);

                foreach (var change in parentChanges.ToConcreteType())
                {
                    switch (change.Reason)
                    {
                        case ChangeReason.Add or ChangeReason.Update:
                            setChild(change.Key, _reEvaluator(change.Current, change.Key));
                            break;

                        case ChangeReason.Remove:
                            setChild(change.Key, null);
                            break;
                    }
                }
            },
            onChild: (_, parentKey) => cache.Refresh(parentKey),
            tryEmit: observer =>
            {
                var captured = cache.CaptureChanges();
                if (captured.Count > 0)
                {
                    observer.OnNext(captured);
                }
            });

        return buffer is null
            ? changes
            : changes.Buffer(buffer.Value, _scheduler)
                     .Where(static batches => batches.Count > 0)
                     .Select(static batches => new ChangeSet<TObject, TKey>(batches.SelectMany(static cs => cs)));
    });
}
