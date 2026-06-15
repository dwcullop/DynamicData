// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Pairs a source item with its latest emitted value (or none), used by
/// <see cref="TrueFor{T,TValue}"/>.
/// </summary>
internal sealed class ItemWithLatest<T, TValue>(T item)
    where T : notnull
    where TValue : notnull
{
    public T Item { get; } = item;

    public Optional<TValue> LatestValue { get; set; } = Optional<TValue>.None;
}
