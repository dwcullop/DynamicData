// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

internal sealed class TransformOnObservable<TSource, TDestination>(IObservable<IChangeSet<TSource>> source, Func<TSource, IObservable<TDestination>> transform)
    where TSource : notnull
    where TDestination : notnull
{
    private readonly IObservable<IChangeSet<TSource>> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Func<TSource, IObservable<TDestination>> _transform = transform ?? throw new ArgumentNullException(nameof(transform));

    public IObservable<IChangeSet<TDestination>> Run() =>
        _source
            .Orchestrate<TSource, TDestination, IChangeSet<TransformedValue<TSource, TDestination>>>(
                (ctx, _) => new TransformOnObservableOrchestrator<TSource, TDestination>(_transform))
            .Filter(v => v.HasValue)
            .Transform(v => v.Value.Value)
            .SuppressRefresh()
            .NotEmpty();
}
