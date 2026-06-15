// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

// ReSharper disable once CheckNamespace
namespace DynamicData;

/// <summary>
/// Type-filter extension on list changesets.
/// </summary>
public static partial class ObservableListEx
{
    /// <summary>
    /// Filters and casts items in the changeset to <typeparamref name="TDestination"/>. Items
    /// that are not of type <typeparamref name="TDestination"/> are excluded.
    /// </summary>
    /// <typeparam name="TObject">The source item type.</typeparam>
    /// <typeparam name="TDestination">The destination type to filter and cast to.</typeparam>
    /// <param name="source">The source list changeset.</param>
    /// <returns>A list changeset of <typeparamref name="TDestination"/> items.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static IObservable<IChangeSet<TDestination>> OfType<TObject, TDestination>(
        this IObservable<IChangeSet<TObject>> source)
        where TObject : notnull
        where TDestination : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));

        return source.Filter(o => o is TDestination).Transform(o => (TDestination)(object)o);
    }
}
