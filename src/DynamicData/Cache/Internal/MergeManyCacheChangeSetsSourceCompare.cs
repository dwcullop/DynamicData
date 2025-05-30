// Copyright (c) 2011-2023 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.Cache.Internal;

/// <summary>
/// Alternate version of MergeManyCacheChangeSets that uses a Comparer of the source, not the destination type
/// So that items from the most important source go into the resulting changeset.
/// </summary>
internal sealed class MergeManyCacheChangeSetsSourceCompare<TObject, TKey, TDestination, TDestinationKey>(IObservable<IChangeSet<TObject, TKey>> source, Func<TObject, TKey, IObservable<IChangeSet<TDestination, TDestinationKey>>> selector, IComparer<TObject> parentCompare, IEqualityComparer<TDestination>? equalityComparer, IComparer<TDestination>? childCompare, bool reevalOnRefresh = false)
    where TObject : notnull
    where TKey : notnull
    where TDestination : notnull
    where TDestinationKey : notnull
{
    private readonly MergeManyCacheChangeSets<TObject, TKey, ParentChildEntry, TDestinationKey> _mergeManyCacheChangeSets = new(
        source,
        (obj, key) => selector(obj, key).Transform(dest => new ParentChildEntry(obj, dest)),
        (equalityComparer != null) ? new ParentChildEqualityCompare(equalityComparer) : null,
        (childCompare is null) ? new ParentOnlyCompare(parentCompare) : new ParentChildCompare(parentCompare, childCompare),
        reevalOnRefresh);

    public IObservable<IChangeSet<TDestination, TDestinationKey>> Run() => _mergeManyCacheChangeSets.Run().TransformImmutable(entry => entry.Child);

    private sealed record ParentChildEntry(TObject Parent, TDestination Child);

    private sealed class ParentChildCompare(IComparer<TObject> comparerParent, IComparer<TDestination> comparerChild) : Comparer<ParentChildEntry>
    {
        public override int Compare(ParentChildEntry? x, ParentChildEntry? y) => (x, y) switch
        {
            (not null, not null) => comparerParent.Compare(x.Parent, y.Parent) switch
                                    {
                                        0 => comparerChild.Compare(x.Child, y.Child),
                                        int i => i,
                                    },
            (null, null) => 0,
            (null, not null) => 1,
            (not null, null) => -1,
        };
    }

    private sealed class ParentOnlyCompare(IComparer<TObject> comparer) : Comparer<ParentChildEntry>
    {
        public override int Compare(ParentChildEntry? x, ParentChildEntry? y) => (x, y) switch
        {
            (not null, not null) => comparer.Compare(x.Parent, y.Parent),
            (null, null) => 0,
            (null, not null) => 1,
            (not null, null) => -1,
        };
    }

    private sealed class ParentChildEqualityCompare(IEqualityComparer<TDestination> comparer) : EqualityComparer<ParentChildEntry>
    {
        public override bool Equals(ParentChildEntry? x, ParentChildEntry? y) => (x, y) switch
        {
            (not null, not null) => comparer.Equals(x.Child, y.Child),
            (null, null) => true,
            _ => false,
        };

        public override int GetHashCode(ParentChildEntry obj) => comparer.GetHashCode(obj.Child);
    }
}
