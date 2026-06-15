// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

internal sealed class TransformManyAsync<TSource, TDestination>(IObservable<IChangeSet<TSource>> source, Func<TSource, Task<IObservable<IChangeSet<TDestination>>>> transformer, IEqualityComparer<TDestination>? equalityComparer)
    where TSource : notnull
    where TDestination : notnull
{
    private readonly IObservable<IChangeSet<TSource>> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Func<TSource, Task<IObservable<IChangeSet<TDestination>>>> _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    private readonly IEqualityComparer<TDestination>? _equalityComparer = equalityComparer;

    public IObservable<IChangeSet<TDestination>> Run() =>
        _source.Orchestrate<TSource, IChangeSet<TDestination>, IChangeSet<TDestination>>(
            (ctx, _) => new TransformManyAsyncOrchestrator<TSource, TDestination>(_transformer, _equalityComparer));
}
