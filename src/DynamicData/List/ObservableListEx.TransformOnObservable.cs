// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using DynamicData.List.Internal;

// ReSharper disable once CheckNamespace
namespace DynamicData;

/// <summary>
/// Extension methods for <see cref="IObservable{T}"/> with a list changeset that transform each
/// item via a per-item observable.
/// </summary>
public static partial class ObservableListEx
{
    /// <summary>
    /// Transforms each item in a list changeset into a downstream value supplied by a per-item
    /// observable. The output list contains one entry per source item whose transform observable
    /// has emitted at least one value; the entry is the latest emitted value. When a source item
    /// is removed, its entry is removed.
    /// </summary>
    /// <typeparam name="TSource">The source item type.</typeparam>
    /// <typeparam name="TDestination">The destination item type.</typeparam>
    /// <param name="source">The source list changeset.</param>
    /// <param name="transformFactory">A function that produces a per-item observable of destination values.</param>
    /// <returns>An observable list changeset of destination values.</returns>
    /// <exception cref="ArgumentNullException">Either <paramref name="source"/> or <paramref name="transformFactory"/> is <see langword="null"/>.</exception>
    public static IObservable<IChangeSet<TDestination>> TransformOnObservable<TSource, TDestination>(
        this IObservable<IChangeSet<TSource>> source,
        Func<TSource, IObservable<TDestination>> transformFactory)
        where TSource : notnull
        where TDestination : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        transformFactory.ThrowArgumentNullExceptionIfNull(nameof(transformFactory));

        return new TransformOnObservable<TSource, TDestination>(source, transformFactory).Run();
    }
}
