// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Pair of an item value and its filter inclusion state. Used internally by
/// <see cref="FilterOnObservable{T}"/> and its orchestrator.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal readonly struct ObjWithFilterValue<T> : IEquatable<ObjWithFilterValue<T>>
    where T : notnull
{
    public ObjWithFilterValue(T obj, bool filter)
    {
        Obj = obj;
        Filter = filter;
    }

    public T Obj { get; }

    public bool Filter { get; }

    private static IEqualityComparer<ObjWithFilterValue<T>> ObjComparer { get; } = new ObjEqualityComparer();

    public bool Equals(ObjWithFilterValue<T> other) => ObjComparer.Equals(this, other);

    public override bool Equals(object? obj) => obj is ObjWithFilterValue<T> value && Equals(value);

    public override int GetHashCode() => ObjComparer.GetHashCode(this);

    private sealed class ObjEqualityComparer : IEqualityComparer<ObjWithFilterValue<T>>
    {
        public bool Equals(ObjWithFilterValue<T> x, ObjWithFilterValue<T> y) => EqualityComparer<T>.Default.Equals(x.Obj, y.Obj);

        public int GetHashCode(ObjWithFilterValue<T> obj) =>
            obj.Obj is null ? 0 : EqualityComparer<T>.Default.GetHashCode(obj.Obj) * 397;
    }
}
