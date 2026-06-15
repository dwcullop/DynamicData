// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DynamicData.List.Internal;

internal sealed class AutoRefresh<TObject, TAny>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<TAny>> reEvaluator, TimeSpan? buffer = null, IScheduler? scheduler = null)
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<TAny>> _reEvaluator = reEvaluator ?? throw new ArgumentNullException(nameof(reEvaluator));
    private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<IChangeSet<TObject>> Run() =>
        buffer is null
            ? _source.Orchestrate<TObject, TAny, IChangeSet<TObject>>((ctx, _) => new AutoRefreshOrchestrator<TObject, TAny>(_reEvaluator))
            : RunBuffered();

    private IObservable<IChangeSet<TObject>> RunBuffered() => Observable.Create<IChangeSet<TObject>>(
            observer =>
            {
                var locker = InternalEx.NewLock();

                var allItems = new List<TObject>();

                var shared = _source.Synchronize(locker).Clone(allItems)
                    .Publish();

                var itemHasChanged = shared.MergeMany((t) => _reEvaluator(t).Select(_ => t));

                IObservable<IEnumerable<TObject>> itemsChanged =
                    itemHasChanged.Buffer(buffer!.Value, scheduler ?? GlobalConfig.DefaultScheduler).Where(list => list.Count > 0);

                IObservable<IChangeSet<TObject>> requiresRefresh = itemsChanged.Synchronize(locker).Select(
                    items =>
                        allItems.IndexOfMany(items, (t, idx) => new Change<TObject>(ListChangeReason.Refresh, t, idx))).Select(changes => new ChangeSet<TObject>(changes));

                var publisher = shared.Merge(requiresRefresh).SubscribeSafe(observer);

                return new CompositeDisposable(publisher, shared.Connect());
            });
}
