// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DynamicData.List.Internal;

internal sealed class FilterOnObservable<TObject>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<bool>> filter, TimeSpan? buffer = null, IScheduler? scheduler = null)
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<bool>> _filter = filter ?? throw new ArgumentNullException(nameof(filter));
    private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<IChangeSet<TObject>> Run() =>
        buffer is null
            ? RunOrchestrated()
            : RunBuffered();

    private IObservable<IChangeSet<TObject>> RunOrchestrated() =>
        _source
            .Orchestrate<TObject, bool, IChangeSet<ObjWithFilterValue<TObject>>>(
                (ctx, _) => new FilterOnObservableOrchestrator<TObject>(_filter))
            .Filter(v => v.Filter)
            .Transform(v => v.Obj)
            .SuppressRefresh()
            .NotEmpty();

    private IObservable<IChangeSet<TObject>> RunBuffered() => Observable.Create<IChangeSet<TObject>>(
            observer =>
            {
                var locker = InternalEx.NewLock();

                var allItems = new List<ObjWithFilterValue<TObject>>();

                var shared = _source.Synchronize(locker).Transform(v => new ObjWithFilterValue<TObject>(v, true))
                    .Clone(allItems)
                    .Publish();

                var itemHasChanged = shared.MergeMany(v => _filter(v.Obj).Select(prop => new ObjWithFilterValue<TObject>(v.Obj, prop)));

                IObservable<IEnumerable<ObjWithFilterValue<TObject>>> itemsChanged =
                    itemHasChanged.Buffer(buffer!.Value, scheduler ?? GlobalConfig.DefaultScheduler).Where(list => list.Count > 0);

                var requiresRefresh = itemsChanged.Synchronize(locker).Select(
                    items =>
                        IndexOfMany(allItems, items, v => v.Obj, (t, idx) => new Change<ObjWithFilterValue<TObject>>(ListChangeReason.Refresh, t, idx))).Select(changes => new ChangeSet<ObjWithFilterValue<TObject>>(changes));

                var publisher = shared.Merge(requiresRefresh).Filter(v => v.Filter)
                    .Transform(v => v.Obj)
                    .SuppressRefresh()
                    .NotEmpty()
                    .SubscribeSafe(observer);

                return new CompositeDisposable(publisher, shared.Connect());
            });

    private static IEnumerable<TResult> IndexOfMany<TObj, TObjectProp, TResult>(IEnumerable<TObj> source, IEnumerable<TObj> itemsToFind, Func<TObj, TObjectProp> objectPropertyFunc, Func<TObj, int, TResult> resultSelector)
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        itemsToFind.ThrowArgumentNullExceptionIfNull(nameof(itemsToFind));
        resultSelector.ThrowArgumentNullExceptionIfNull(nameof(resultSelector));

        var indexed = source.Select((element, index) => new { Element = element, Index = index });
        return itemsToFind.Join(indexed, objectPropertyFunc, right => objectPropertyFunc(right.Element), (left, right) => resultSelector(left, right.Index));
    }
}
