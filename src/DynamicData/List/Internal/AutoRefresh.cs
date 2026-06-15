// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Concurrency;

namespace DynamicData.List.Internal;

internal sealed class AutoRefresh<TObject, TAny>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<TAny>> reEvaluator, TimeSpan? buffer = null, IScheduler? scheduler = null)
    where TObject : notnull
{
    private readonly Func<TObject, IObservable<TAny>> _reEvaluator = reEvaluator ?? throw new ArgumentNullException(nameof(reEvaluator));
    private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));

    public IObservable<IChangeSet<TObject>> Run() =>
        buffer is null
            ? _source.Orchestrate<TObject, TAny, IChangeSet<TObject>>(
                (ctx, _) => new AutoRefreshOrchestrator<TObject, TAny>(_reEvaluator))
            : _source.Orchestrate<TObject, TAny, IChangeSet<TObject>>(
                (ctx, emitter) => new BufferedAutoRefreshOrchestrator<TObject, TAny>(
                    _reEvaluator, buffer.Value, scheduler ?? GlobalConfig.DefaultScheduler, ctx, emitter));
}
