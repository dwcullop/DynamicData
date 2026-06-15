// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using DynamicData.List.Internal;

// ReSharper disable once CheckNamespace
namespace DynamicData;

/// <summary>
/// Extension methods for grouping list items by a per-item observable key.
/// </summary>
public static partial class ObservableListEx
{
    /// <summary>
    /// Groups each item by a key produced by a per-item observable. Items move between groups
    /// when their associated observable emits a new key. Groups are created on first use and
    /// removed when emptied.
    /// </summary>
    /// <typeparam name="TObject">The source item type.</typeparam>
    /// <typeparam name="TGroupKey">The group key type.</typeparam>
    /// <param name="source">The source list changeset.</param>
    /// <param name="groupKeySelector">A function that produces a per-item observable of group keys.</param>
    /// <returns>An observable list changeset of groups.</returns>
    /// <exception cref="ArgumentNullException">Either <paramref name="source"/> or <paramref name="groupKeySelector"/> is <see langword="null"/>.</exception>
    public static IObservable<IChangeSet<IGroup<TObject, TGroupKey>>> GroupOnObservable<TObject, TGroupKey>(
        this IObservable<IChangeSet<TObject>> source,
        Func<TObject, IObservable<TGroupKey>> groupKeySelector)
        where TObject : notnull
        where TGroupKey : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        groupKeySelector.ThrowArgumentNullExceptionIfNull(nameof(groupKeySelector));

        return new GroupOnObservable<TObject, TGroupKey>(source, groupKeySelector).Run();
    }
}
