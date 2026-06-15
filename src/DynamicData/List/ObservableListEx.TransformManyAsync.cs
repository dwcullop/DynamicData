// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using DynamicData.List.Internal;

// ReSharper disable once CheckNamespace
namespace DynamicData;

/// <summary>
/// Extension methods for async per-item transformation into a child list changeset, then merged.
/// </summary>
public static partial class ObservableListEx
{
    /// <summary>
    /// Asynchronously transforms each source item into a child list changeset, then merges all
    /// child changes into a single output list changeset. When a source item is removed, its
    /// contributed entries are removed from the merged output.
    /// </summary>
    /// <typeparam name="TSource">The source item type.</typeparam>
    /// <typeparam name="TDestination">The destination item type.</typeparam>
    /// <param name="source">The source list changeset.</param>
    /// <param name="transformer">An async function that produces a per-item child list changeset.</param>
    /// <param name="equalityComparer">Optional equality comparer for the merged result.</param>
    /// <returns>An observable list changeset of destination items.</returns>
    /// <exception cref="ArgumentNullException">Either <paramref name="source"/> or <paramref name="transformer"/> is <see langword="null"/>.</exception>
    public static IObservable<IChangeSet<TDestination>> TransformManyAsync<TSource, TDestination>(
        this IObservable<IChangeSet<TSource>> source,
        Func<TSource, Task<IObservable<IChangeSet<TDestination>>>> transformer,
        IEqualityComparer<TDestination>? equalityComparer = null)
        where TSource : notnull
        where TDestination : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        transformer.ThrowArgumentNullExceptionIfNull(nameof(transformer));

        return new TransformManyAsync<TSource, TDestination>(source, transformer, equalityComparer).Run();
    }
}
