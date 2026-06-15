// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using DynamicData.List.Internal;

// ReSharper disable once CheckNamespace
namespace DynamicData;

/// <summary>
/// Aggregation extension methods on list changesets that evaluate a condition across all items
/// using per-item observables.
/// </summary>
public static partial class ObservableListEx
{
    /// <summary>
    /// Produces a boolean observable that emits whenever the all-items condition changes. The
    /// condition is true when every source item's most recent per-item observable value
    /// satisfies <paramref name="equalityCondition"/>. Items whose observable has not yet emitted
    /// are treated as not satisfying the condition. An empty source is vacuously true.
    /// </summary>
    /// <typeparam name="TObject">The source item type.</typeparam>
    /// <typeparam name="TValue">The per-item observable value type.</typeparam>
    /// <param name="source">The source list changeset.</param>
    /// <param name="observableSelector">Factory that produces a per-item observable.</param>
    /// <param name="equalityCondition">Predicate applied to each per-item observable's latest value.</param>
    /// <returns>A bool observable that emits whenever the aggregate result changes.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static IObservable<bool> TrueForAll<TObject, TValue>(
        this IObservable<IChangeSet<TObject>> source,
        Func<TObject, IObservable<TValue>> observableSelector,
        Func<TValue, bool> equalityCondition)
        where TObject : notnull
        where TValue : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        observableSelector.ThrowArgumentNullExceptionIfNull(nameof(observableSelector));
        equalityCondition.ThrowArgumentNullExceptionIfNull(nameof(equalityCondition));

        return new TrueFor<TObject, TValue>(
            source,
            observableSelector,
            items => items.All(i => i.LatestValue.HasValue && equalityCondition(i.LatestValue.Value))).Run();
    }

    /// <summary>
    /// Produces a boolean observable that emits whenever the any-item condition changes. The
    /// condition is true when at least one source item's most recent per-item observable value
    /// satisfies <paramref name="equalityCondition"/>.
    /// </summary>
    /// <typeparam name="TObject">The source item type.</typeparam>
    /// <typeparam name="TValue">The per-item observable value type.</typeparam>
    /// <param name="source">The source list changeset.</param>
    /// <param name="observableSelector">Factory that produces a per-item observable.</param>
    /// <param name="equalityCondition">Predicate applied to each per-item observable's latest value.</param>
    /// <returns>A bool observable that emits whenever the aggregate result changes.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static IObservable<bool> TrueForAny<TObject, TValue>(
        this IObservable<IChangeSet<TObject>> source,
        Func<TObject, IObservable<TValue>> observableSelector,
        Func<TValue, bool> equalityCondition)
        where TObject : notnull
        where TValue : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        observableSelector.ThrowArgumentNullExceptionIfNull(nameof(observableSelector));
        equalityCondition.ThrowArgumentNullExceptionIfNull(nameof(equalityCondition));

        return new TrueFor<TObject, TValue>(
            source,
            observableSelector,
            items => items.Any(i => i.LatestValue.HasValue && equalityCondition(i.LatestValue.Value))).Run();
    }
}
