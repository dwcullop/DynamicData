// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reactive.Subjects;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class GroupOnObservableFixture
{
    [Fact]
    public void ItemAppearsInGroupAfterFirstKeyEmission()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .GroupOnObservable(i => i.GroupKey)
            .AsAggregator();

        source.Add(item);
        results.Data.Count.Should().Be(0, "no group key emitted yet");

        item.SetGroup("A");

        results.Data.Count.Should().Be(1);
        results.Data.Items.Single().GroupKey.Should().Be("A");
        results.Data.Items.Single().List.Items.Should().BeEquivalentTo(new[] { item });
    }

    [Fact]
    public void ItemMovesBetweenGroupsOnKeyChange()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .GroupOnObservable(i => i.GroupKey)
            .AsAggregator();

        source.Add(item);
        item.SetGroup("A");
        item.SetGroup("B");

        results.Data.Count.Should().Be(1, "group A should be empty and removed");
        results.Data.Items.Single().GroupKey.Should().Be("B");
        results.Data.Items.Single().List.Items.Should().BeEquivalentTo(new[] { item });
    }

    [Fact]
    public void MultipleItemsShareGroup()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();
        var c = new Item();

        using var results = source.Connect()
            .GroupOnObservable(i => i.GroupKey)
            .AsAggregator();

        source.AddRange(new[] { a, b, c });
        a.SetGroup("X");
        b.SetGroup("X");
        c.SetGroup("Y");

        results.Data.Count.Should().Be(2);
        var groupX = results.Data.Items.Single(g => g.GroupKey == "X");
        var groupY = results.Data.Items.Single(g => g.GroupKey == "Y");

        groupX.List.Items.Should().BeEquivalentTo(new[] { a, b });
        groupY.List.Items.Should().BeEquivalentTo(new[] { c });
    }

    [Fact]
    public void SourceRemove_ItemDisappearsFromGroup()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .GroupOnObservable(i => i.GroupKey)
            .AsAggregator();

        source.Add(item);
        item.SetGroup("A");

        source.Remove(item);
        results.Data.Count.Should().Be(0, "group A should be empty and removed");
    }

    [Fact]
    public void EmptyingGroup_RemovesGroup()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        using var results = source.Connect()
            .GroupOnObservable(i => i.GroupKey)
            .AsAggregator();

        source.AddRange(new[] { a, b });
        a.SetGroup("X");
        b.SetGroup("Y");

        results.Data.Count.Should().Be(2);

        a.SetGroup("Y");

        results.Data.Count.Should().Be(1, "X is empty and removed; both items in Y");
        var groupY = results.Data.Items.Single();
        groupY.GroupKey.Should().Be("Y");
        groupY.List.Items.Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void Clear_AllGroupsRemoved()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        using var results = source.Connect()
            .GroupOnObservable(i => i.GroupKey)
            .AsAggregator();

        source.AddRange(new[] { a, b });
        a.SetGroup("X");
        b.SetGroup("Y");

        source.Clear();

        results.Data.Count.Should().Be(0);
    }

    [Fact]
    public void NullSourceOrSelector_Throws()
    {
        var source = new SourceList<Item>();

        Action act1 = () => ObservableListEx.GroupOnObservable<Item, string>(null!, _ => null!);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => source.Connect().GroupOnObservable<Item, string>(null!);
        act2.Should().Throw<ArgumentNullException>();
    }

    private sealed class Item : IDisposable
    {
        private readonly Subject<string> _groupKey = new();

        public IObservable<string> GroupKey => _groupKey;

        public void SetGroup(string key) => _groupKey.OnNext(key);

        public void Dispose() => _groupKey.Dispose();
    }
}
