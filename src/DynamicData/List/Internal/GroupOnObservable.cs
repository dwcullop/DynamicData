// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

internal sealed class GroupOnObservable<TObject, TGroupKey>(IObservable<IChangeSet<TObject>> source, Func<TObject, IObservable<TGroupKey>> groupKeySelector)
    where TObject : notnull
    where TGroupKey : notnull
{
    private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Func<TObject, IObservable<TGroupKey>> _groupKeySelector = groupKeySelector ?? throw new ArgumentNullException(nameof(groupKeySelector));

    public IObservable<IChangeSet<IGroup<TObject, TGroupKey>>> Run() =>
        _source.Orchestrate<TObject, TGroupKey, IChangeSet<IGroup<TObject, TGroupKey>>>(
            (ctx, _) => new GroupOnObservableOrchestrator<TObject, TGroupKey>(_groupKeySelector));
}
