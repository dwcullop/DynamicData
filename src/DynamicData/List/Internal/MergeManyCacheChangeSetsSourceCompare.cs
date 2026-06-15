// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;

using DynamicData.Cache.Internal;

namespace DynamicData.List.Internal;

/// <summary>
/// Variant of <see cref="MergeManyCacheChangeSets{TObject,TDestination,TDestinationKey}"/> that
/// uses a comparer on the source (parent) items for conflict resolution: when the same
/// destination key appears in multiple parents' child changesets, the parent comparer picks
/// the winner.
/// </summary>
internal sealed class MergeManyCacheChangeSetsSourceCompare<TObject, TDestination, TDestinationKey>(
    IObservable<IChangeSet<TObject>> source,
    Func<TObject, IObservable<IChangeSet<TDestination, TDestinationKey>>> selector,
    IComparer<TObject> parentCompare,
    IEqualityComparer<TDestination>? equalityComparer,
    IComparer<TDestination>? childCompare)
    where TObject : notnull
    where TDestination : notnull
    where TDestinationKey : notnull
{
    private readonly IObservable<IChangeSet<TObject>> _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Func<TObject, IObservable<IChangeSet<TDestination, TDestinationKey>>> _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    private readonly IComparer<TObject> _parentCompare = parentCompare ?? throw new ArgumentNullException(nameof(parentCompare));
    private readonly IEqualityComparer<TDestination>? _equalityComparer = equalityComparer;
    private readonly IComparer<TDestination>? _childCompare = childCompare;

    public IObservable<IChangeSet<TDestination, TDestinationKey>> Run() =>
        _source
            .Orchestrate<TObject, IChangeSet<ParentChildEntry, TDestinationKey>, IChangeSet<ParentChildEntry, TDestinationKey>>(
                (ctx, _) => new MergeManyCacheChangeSetsOrchestrator<TObject, ParentChildEntry, TDestinationKey>(
                    selector: obj => _selector(obj).Transform(child => new ParentChildEntry(obj, child)),
                    equalityComparer: BuildEqualityComparer(),
                    comparer: BuildComparer()))
            .TransformImmutable(entry => entry.Child);

    private IComparer<ParentChildEntry> BuildComparer() =>
        _childCompare is null
            ? new ParentOnlyCompare(_parentCompare)
            : new ParentChildCompare(_parentCompare, _childCompare);

    private IEqualityComparer<ParentChildEntry>? BuildEqualityComparer() =>
        _equalityComparer is null ? null : new ParentChildEqualityCompare(_equalityComparer);

    internal sealed class ParentChildEntry(TObject parent, TDestination child)
    {
        public TObject Parent { get; } = parent;

        public TDestination Child { get; } = child;
    }

    private sealed class ParentOnlyCompare(IComparer<TObject> parentCompare) : IComparer<ParentChildEntry>
    {
        public int Compare(ParentChildEntry? x, ParentChildEntry? y)
        {
            if (x is null) return y is null ? 0 : -1;
            if (y is null) return 1;
            return parentCompare.Compare(x.Parent, y.Parent);
        }
    }

    private sealed class ParentChildCompare(IComparer<TObject> parentCompare, IComparer<TDestination> childCompare) : IComparer<ParentChildEntry>
    {
        public int Compare(ParentChildEntry? x, ParentChildEntry? y)
        {
            if (x is null) return y is null ? 0 : -1;
            if (y is null) return 1;
            var parentResult = parentCompare.Compare(x.Parent, y.Parent);
            return parentResult != 0 ? parentResult : childCompare.Compare(x.Child, y.Child);
        }
    }

    private sealed class ParentChildEqualityCompare(IEqualityComparer<TDestination> childEquality) : IEqualityComparer<ParentChildEntry>
    {
        public bool Equals(ParentChildEntry? x, ParentChildEntry? y)
        {
            if (x is null) return y is null;
            if (y is null) return false;
            return childEquality.Equals(x.Child, y.Child);
        }

        public int GetHashCode(ParentChildEntry obj) => childEquality.GetHashCode(obj.Child);
    }
}
