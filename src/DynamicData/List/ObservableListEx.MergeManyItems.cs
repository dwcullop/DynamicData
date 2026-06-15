// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using DynamicData.List.Internal;

// ReSharper disable once CheckNamespace
namespace DynamicData;

/// <summary>
/// Extension methods for merging per-item inner observables while preserving the originating item.
/// </summary>
public static partial class ObservableListEx
{
    /// <summary>
    /// Subscribes to a per-item observable for each item in the source. Each emission is paired
    /// with the source item via <see cref="ItemWithValue{TObject,TValue}"/>. When a source item
    /// is removed, its inner subscription is disposed.
    /// </summary>
    /// <typeparam name="TObject">The source item type.</typeparam>
    /// <typeparam name="TDestination">The inner observable value type.</typeparam>
    /// <param name="source">The source list changeset.</param>
    /// <param name="observableSelector">A function that produces a per-item observable.</param>
    /// <returns>An observable of <see cref="ItemWithValue{TObject,TValue}"/> emissions.</returns>
    /// <exception cref="ArgumentNullException">Either <paramref name="source"/> or <paramref name="observableSelector"/> is <see langword="null"/>.</exception>
    public static IObservable<ItemWithValue<TObject, TDestination>> MergeManyItems<TObject, TDestination>(
        this IObservable<IChangeSet<TObject>> source,
        Func<TObject, IObservable<TDestination>> observableSelector)
        where TObject : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        observableSelector.ThrowArgumentNullExceptionIfNull(nameof(observableSelector));

        return new MergeManyItems<TObject, TDestination>(source, observableSelector).Run();
    }
}
