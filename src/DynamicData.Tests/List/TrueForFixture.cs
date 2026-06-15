// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class TrueForFixture
{
    [Fact]
    public void TrueForAll_EmitsTrueWhenAllItemsSatisfy()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        var received = new List<bool>();
        using var sub = source.Connect()
            .TrueForAll(i => i.Value, v => v > 0)
            .Subscribe(received.Add);

        source.AddRange(new[] { a, b });

        a.Emit(1);
        received.Last().Should().BeFalse("b hasn't emitted yet");

        b.Emit(1);
        received.Last().Should().BeTrue();

        b.Emit(-1);
        received.Last().Should().BeFalse();

        b.Emit(2);
        received.Last().Should().BeTrue();
    }

    [Fact]
    public void TrueForAll_EmptySource_NoEmissions()
    {
        // Matches cache TrueForAll behavior: with no items the per-item observable stream
        // never emits, so CombineLatest semantics require waiting indefinitely. This is
        // intentional and consistent with the cache version.
        var source = new SourceList<Item>();

        var received = new List<bool>();
        using var sub = source.Connect()
            .TrueForAll(i => i.Value, v => v > 0)
            .Subscribe(received.Add);

        received.Should().BeEmpty();
    }

    [Fact]
    public void TrueForAny_EmitsTrueWhenAnyItemSatisfies()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        var received = new List<bool>();
        using var sub = source.Connect()
            .TrueForAny(i => i.Value, v => v > 100)
            .Subscribe(received.Add);

        source.AddRange(new[] { a, b });

        a.Emit(10);
        received.Last().Should().BeFalse();

        b.Emit(200);
        received.Last().Should().BeTrue();

        b.Emit(0);
        received.Last().Should().BeFalse();
    }

    [Fact]
    public void RemovingItem_RecalculatesAggregate()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        var received = new List<bool>();
        using var sub = source.Connect()
            .TrueForAll(i => i.Value, v => v > 0)
            .Subscribe(received.Add);

        source.AddRange(new[] { a, b });
        a.Emit(1);
        // b hasn't emitted -> aggregate is false
        received.Last().Should().BeFalse();

        source.Remove(b);
        received.Last().Should().BeTrue("only 'a' remains and satisfies the condition");
    }

    [Fact]
    public void NullArgs_Throw()
    {
        var source = new SourceList<Item>();

        Action act1 = () => ObservableListEx.TrueForAll<Item, int>(null!, _ => null!, _ => true);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => source.Connect().TrueForAll<Item, int>(null!, _ => true);
        act2.Should().Throw<ArgumentNullException>();

        Action act3 = () => source.Connect().TrueForAll(i => i.Value, (Func<int, bool>)null!);
        act3.Should().Throw<ArgumentNullException>();
    }

    private sealed class Item : IDisposable
    {
        private readonly Subject<int> _value = new();

        public IObservable<int> Value => _value;

        public void Emit(int value) => _value.OnNext(value);

        public void Dispose() => _value.Dispose();
    }
}
