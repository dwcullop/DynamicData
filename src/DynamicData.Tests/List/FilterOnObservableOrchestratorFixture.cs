// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reactive.Subjects;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class FilterOnObservableOrchestratorFixture
{
    [Fact]
    public void Add_DefaultsIncludedThenFilterObservableTransitionsState()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .FilterOnObservable(i => i.IsIncluded)
            .AsAggregator();

        source.Add(item);
        results.Data.Count.Should().Be(1, "default state is included until filter says otherwise");

        item.SetIncluded(false);
        results.Data.Count.Should().Be(0);

        item.SetIncluded(true);
        results.Data.Count.Should().Be(1);
    }

    [Fact]
    public void Remove_ItemDisappears()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .FilterOnObservable(i => i.IsIncluded)
            .AsAggregator();

        source.Add(item);
        item.SetIncluded(true);

        source.Remove(item);
        results.Data.Count.Should().Be(0);
    }

    [Fact]
    public void Replace_OldDisappearsAndNewAppears()
    {
        var source = new SourceList<Item>();
        var oldItem = new Item();
        var newItem = new Item();

        using var results = source.Connect()
            .FilterOnObservable(i => i.IsIncluded)
            .AsAggregator();

        source.Add(oldItem);
        oldItem.SetIncluded(true);

        source.Replace(oldItem, newItem);
        newItem.SetIncluded(true);

        results.Data.Count.Should().Be(1);
        results.Data.Items.Should().Contain(newItem);
        results.Data.Items.Should().NotContain(oldItem);
    }

    [Fact]
    public void Clear_AllItemsDisappear()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();
        a.SetIncluded(true);
        b.SetIncluded(true);

        using var results = source.Connect()
            .FilterOnObservable(i => i.IsIncluded)
            .AsAggregator();

        source.AddRange(new[] { a, b });
        source.Clear();

        results.Data.Count.Should().Be(0);
    }

    private sealed class Item : IDisposable
    {
        private readonly BehaviorSubject<bool> _isIncluded = new(true);

        public IObservable<bool> IsIncluded => _isIncluded;

        public void SetIncluded(bool value) => _isIncluded.OnNext(value);

        public void Dispose() => _isIncluded.Dispose();
    }
}
