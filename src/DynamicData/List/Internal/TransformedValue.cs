// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Per-slot intermediate value used by <see cref="TransformOnObservable{TSource,TDestination}"/>.
/// Carries the latest emitted transform value (or none) for a source slot. Equality is keyed
/// off the source item only; the destination value is metadata for the downstream Filter step.
/// </summary>
internal readonly struct TransformedValue<TSource, TDestination> : IEquatable<TransformedValue<TSource, TDestination>>
    where TSource : notnull
    where TDestination : notnull
{
    public TransformedValue(TSource source, Optional<TDestination> value)
    {
        Source = source;
        Value = value;
    }

    public TSource Source { get; }

    public Optional<TDestination> Value { get; }

    public bool HasValue => Value.HasValue;

    public bool Equals(TransformedValue<TSource, TDestination> other) =>
        EqualityComparer<TSource>.Default.Equals(Source, other.Source);

    public override bool Equals(object? obj) => obj is TransformedValue<TSource, TDestination> v && Equals(v);

    public override int GetHashCode() =>
        Source is null ? 0 : EqualityComparer<TSource>.Default.GetHashCode(Source);
}
