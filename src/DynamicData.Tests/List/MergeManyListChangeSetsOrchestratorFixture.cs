// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Linq;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class MergeManyListChangeSetsOrchestratorFixture
{
    [Fact]
    public void ChildListChanges_MergeIntoDownstream()
    {
        var parent = new SourceList<SourceList<int>>();
        var childA = new SourceList<int>();
        var childB = new SourceList<int>();

        using var results = parent.Connect()
            .MergeManyChangeSets(c => c.Connect())
            .AsAggregator();

        parent.Add(childA);
        parent.Add(childB);

        childA.AddRange(new[] { 1, 2, 3 });
        childB.AddRange(new[] { 10, 20 });

        results.Data.Items.Should().BeEquivalentTo(new[] { 1, 2, 3, 10, 20 });
    }

    [Fact]
    public void ParentRemove_RemovesAllChildItemsFromDownstream()
    {
        var parent = new SourceList<SourceList<int>>();
        var childA = new SourceList<int>();
        var childB = new SourceList<int>();

        using var results = parent.Connect()
            .MergeManyChangeSets(c => c.Connect())
            .AsAggregator();

        parent.AddRange(new[] { childA, childB });
        childA.AddRange(new[] { 1, 2 });
        childB.AddRange(new[] { 10, 20 });

        parent.Remove(childA);

        results.Data.Items.Should().BeEquivalentTo(new[] { 10, 20 });
    }

    [Fact]
    public void ParentReplace_RemovesOldChildItemsAndAddsNewChildItems()
    {
        var parent = new SourceList<SourceList<int>>();
        var oldChild = new SourceList<int>();
        var newChild = new SourceList<int>();
        oldChild.AddRange(new[] { 1, 2 });
        newChild.AddRange(new[] { 100, 200 });

        using var results = parent.Connect()
            .MergeManyChangeSets(c => c.Connect())
            .AsAggregator();

        parent.Add(oldChild);
        parent.Replace(oldChild, newChild);

        results.Data.Items.Should().BeEquivalentTo(new[] { 100, 200 });
    }

    [Fact]
    public void ChildRemove_RemovesItemFromDownstream()
    {
        var parent = new SourceList<SourceList<int>>();
        var child = new SourceList<int>();
        child.AddRange(new[] { 1, 2, 3 });

        using var results = parent.Connect()
            .MergeManyChangeSets(c => c.Connect())
            .AsAggregator();

        parent.Add(child);
        child.Remove(2);

        results.Data.Items.Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public void ParentClear_RemovesAllChildItems()
    {
        var parent = new SourceList<SourceList<int>>();
        var childA = new SourceList<int>();
        var childB = new SourceList<int>();
        childA.AddRange(new[] { 1, 2 });
        childB.AddRange(new[] { 10, 20 });

        using var results = parent.Connect()
            .MergeManyChangeSets(c => c.Connect())
            .AsAggregator();

        parent.AddRange(new[] { childA, childB });
        parent.Clear();

        results.Data.Items.Should().BeEmpty();
    }

    [Fact]
    public void EqualityComparer_KeepsDuplicatesByDefault()
    {
        var parent = new SourceList<SourceList<int>>();
        var childA = new SourceList<int>();
        var childB = new SourceList<int>();
        childA.AddRange(new[] { 1, 2 });
        childB.AddRange(new[] { 2, 3 });

        using var results = parent.Connect()
            .MergeManyChangeSets(c => c.Connect(), System.Collections.Generic.EqualityComparer<int>.Default)
            .AsAggregator();

        parent.AddRange(new[] { childA, childB });

        results.Data.Items.Should().HaveCount(4);
    }
}
