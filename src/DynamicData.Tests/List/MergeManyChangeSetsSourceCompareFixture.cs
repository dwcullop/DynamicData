// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class MergeManyChangeSetsSourceCompareFixture
{
    [Fact]
    public void ParentComparer_DecidesWinnerWhenSameDestinationKeyAppearsInMultipleChildren()
    {
        var parentA = new Parent(priority: 1);
        var parentB = new Parent(priority: 2);
        parentA.Children.AddOrUpdate(new Child(id: 100, value: "from-A"));
        parentB.Children.AddOrUpdate(new Child(id: 100, value: "from-B"));

        var source = new SourceList<Parent>();

        using var results = source.Connect()
            .MergeManyChangeSets(p => p.Children.Connect(), Comparer<Parent>.Create((x, y) => x.Priority.CompareTo(y.Priority)))
            .AsAggregator();

        source.AddRange(new[] { parentA, parentB });

        results.Data.Items.Should().HaveCount(1);
        results.Data.Items.Single().Value.Should().Be("from-A", "parentA's priority (1) < parentB's (2)");
    }

    [Fact]
    public void ParentRemove_HigherPriorityRemoved_LowerPriorityWins()
    {
        var parentA = new Parent(priority: 1);
        var parentB = new Parent(priority: 2);
        parentA.Children.AddOrUpdate(new Child(id: 100, value: "from-A"));
        parentB.Children.AddOrUpdate(new Child(id: 100, value: "from-B"));

        var source = new SourceList<Parent>();

        using var results = source.Connect()
            .MergeManyChangeSets(p => p.Children.Connect(), Comparer<Parent>.Create((x, y) => x.Priority.CompareTo(y.Priority)))
            .AsAggregator();

        source.AddRange(new[] { parentA, parentB });
        source.Remove(parentA);

        results.Data.Items.Should().HaveCount(1);
        results.Data.Items.Single().Value.Should().Be("from-B");
    }

    [Fact]
    public void DistinctKeysAcrossParents_BothInOutput()
    {
        var parentA = new Parent(priority: 1);
        var parentB = new Parent(priority: 2);
        parentA.Children.AddOrUpdate(new Child(id: 1, value: "A1"));
        parentB.Children.AddOrUpdate(new Child(id: 2, value: "B2"));

        var source = new SourceList<Parent>();

        using var results = source.Connect()
            .MergeManyChangeSets(p => p.Children.Connect(), Comparer<Parent>.Create((x, y) => x.Priority.CompareTo(y.Priority)))
            .AsAggregator();

        source.AddRange(new[] { parentA, parentB });

        results.Data.Items.Should().HaveCount(2);
        results.Data.Items.Select(c => c.Value).Should().BeEquivalentTo(new[] { "A1", "B2" });
    }

    [Fact]
    public void ChildComparer_BreaksTiesWhenParentsCompareEqual()
    {
        var parentA = new Parent(priority: 1);
        var parentB = new Parent(priority: 1);
        parentA.Children.AddOrUpdate(new Child(id: 100, value: "low"));
        parentB.Children.AddOrUpdate(new Child(id: 100, value: "high"));

        var source = new SourceList<Parent>();

        using var results = source.Connect()
            .MergeManyChangeSets(
                p => p.Children.Connect(),
                Comparer<Parent>.Create((x, y) => x.Priority.CompareTo(y.Priority)),
                childComparer: Comparer<Child>.Create((x, y) => string.Compare(x.Value, y.Value, System.StringComparison.Ordinal)))
            .AsAggregator();

        source.AddRange(new[] { parentA, parentB });

        results.Data.Items.Should().HaveCount(1);
        results.Data.Items.Single().Value.Should().Be("high", "'high' < 'low' lexicographically");
    }

    private sealed class Parent
    {
        public Parent(int priority)
        {
            Priority = priority;
            Children = new SourceCache<Child, int>(c => c.Id);
        }

        public int Priority { get; }

        public SourceCache<Child, int> Children { get; }
    }

    private sealed class Child
    {
        public Child(int id, string value)
        {
            Id = id;
            Value = value;
        }

        public int Id { get; }

        public string Value { get; }
    }
}
