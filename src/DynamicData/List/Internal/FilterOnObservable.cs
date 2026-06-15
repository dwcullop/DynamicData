// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace DynamicData.List.Internal;

internal sealed class FilterOnObservable<TObject>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<bool>> filter, TimeSpan? buffer = null, IScheduler? scheduler = null)
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<bool>> _filter = filter ?? throw new ArgumentNullException(nameof(filter));
    private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<IChangeSet<TObject>> Run()
    {
        var inner = buffer is null
            ? _source.Orchestrate<TObject, bool, IChangeSet<ObjWithFilterValue<TObject>>>(
                (ctx, _) => new FilterOnObservableOrchestrator<TObject>(_filter))
            : _source.Orchestrate<TObject, bool, IChangeSet<ObjWithFilterValue<TObject>>>(
                (ctx, emitter) => new BufferedFilterOnObservableOrchestrator<TObject>(
                    _filter, buffer.Value, scheduler ?? GlobalConfig.DefaultScheduler, ctx, emitter));

        return inner
            .Filter(v => v.Filter)
            .Transform(v => v.Obj)
            .SuppressRefresh()
            .NotEmpty();
    }
}
