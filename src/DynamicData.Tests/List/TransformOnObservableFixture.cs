// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Reactive.Subjects;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class TransformOnObservableFixture
{
    [Fact]
    public void TransformObservableEmits_ItemAppearsInOutput()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .TransformOnObservable(i => i.Value)
            .AsAggregator();

        source.Add(item);
        results.Data.Count.Should().Be(0, "no transform value emitted yet");

        item.Emit(42);
        results.Data.Count.Should().Be(1);
        results.Data.Items.Should().BeEquivalentTo(new[] { 42 });
    }

    [Fact]
    public void TransformObservableEmitsMultiple_OutputUpdates()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .TransformOnObservable(i => i.Value)
            .AsAggregator();

        source.Add(item);
        item.Emit(1);
        item.Emit(2);
        item.Emit(3);

        results.Data.Count.Should().Be(1);
        results.Data.Items.Should().BeEquivalentTo(new[] { 3 });
    }

    [Fact]
    public void SourceRemove_OutputRemovesItem()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .TransformOnObservable(i => i.Value)
            .AsAggregator();

        source.Add(item);
        item.Emit(99);
        source.Remove(item);

        results.Data.Count.Should().Be(0);
    }

    [Fact]
    public void MultipleItems_EachInOwnSlot()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();
        var c = new Item();

        using var results = source.Connect()
            .TransformOnObservable(i => i.Value)
            .AsAggregator();

        source.AddRange(new[] { a, b, c });

        a.Emit(10);
        b.Emit(20);
        c.Emit(30);

        results.Data.Count.Should().Be(3);
        results.Data.Items.Should().BeEquivalentTo(new[] { 10, 20, 30 });
    }

    [Fact]
    public void Replace_OldDisappearsAndNewAppearsAfterEmission()
    {
        var source = new SourceList<Item>();
        var oldItem = new Item();
        var newItem = new Item();

        using var results = source.Connect()
            .TransformOnObservable(i => i.Value)
            .AsAggregator();

        source.Add(oldItem);
        oldItem.Emit(100);

        source.Replace(oldItem, newItem);
        results.Data.Count.Should().Be(0, "new item hasn't emitted yet");

        newItem.Emit(200);
        results.Data.Count.Should().Be(1);
        results.Data.Items.Should().BeEquivalentTo(new[] { 200 });
    }

    [Fact]
    public void Clear_AllItemsDisappear()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        using var results = source.Connect()
            .TransformOnObservable(i => i.Value)
            .AsAggregator();

        source.AddRange(new[] { a, b });
        a.Emit(1);
        b.Emit(2);

        source.Clear();
        results.Data.Count.Should().Be(0);
    }

    [Fact]
    public void NullSourceOrSelector_Throws()
    {
        var source = new SourceList<Item>();

        Action act1 = () => ObservableListEx.TransformOnObservable<Item, int>(null!, _ => null!);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => source.Connect().TransformOnObservable<Item, int>(null!);
        act2.Should().Throw<ArgumentNullException>();
    }

    private sealed class Item : IDisposable
    {
        private readonly Subject<int> _value = new();

        public IObservable<int> Value => _value;

        public void Emit(int value) => _value.OnNext(value);

        public void Dispose() => _value.Dispose();
    }
}
