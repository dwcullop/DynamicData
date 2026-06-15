// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reactive.Subjects;

using DynamicData.Kernel;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class MergeManyItemsFixture
{
    [Fact]
    public void InnerValueIsPairedWithSourceItem()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        var received = new List<ItemWithValue<Item, int>>();

        using var sub = source.Connect()
            .MergeManyItems(i => i.Value)
            .Subscribe(received.Add);

        source.Add(item);
        item.Emit(42);

        received.Should().HaveCount(1);
        received[0].Item.Should().BeSameAs(item);
        received[0].Value.Should().Be(42);
    }

    [Fact]
    public void MultipleItems_EachInnerEmissionTaggedWithCorrectItem()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        var received = new List<ItemWithValue<Item, int>>();

        using var sub = source.Connect()
            .MergeManyItems(i => i.Value)
            .Subscribe(received.Add);

        source.AddRange(new[] { a, b });
        a.Emit(1);
        b.Emit(2);

        received.Should().HaveCount(2);
        received[0].Item.Should().BeSameAs(a);
        received[0].Value.Should().Be(1);
        received[1].Item.Should().BeSameAs(b);
        received[1].Value.Should().Be(2);
    }

    [Fact]
    public void Remove_StopsEmissionsFromThatItem()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        var received = new List<ItemWithValue<Item, int>>();

        using var sub = source.Connect()
            .MergeManyItems(i => i.Value)
            .Subscribe(received.Add);

        source.Add(item);
        item.Emit(1);
        source.Remove(item);
        item.Emit(2);

        received.Should().HaveCount(1);
    }

    [Fact]
    public void NullSourceOrSelector_Throws()
    {
        var source = new SourceList<Item>();

        Action act1 = () => ObservableListEx.MergeManyItems<Item, int>(null!, _ => null!);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => source.Connect().MergeManyItems<Item, int>(null!);
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
