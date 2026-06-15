// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;

using DynamicData.List.Internal;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class AutoRefreshOrchestratorFixture
{
    [Fact]
    public void Add_TriggersReevaluatorSubscription_RefreshEmittedAtSlotIndex()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .AutoRefresh(x => x.Value)
            .AsAggregator();

        source.Add(item);
        item.Value = 42;

        results.Messages.Count.Should().Be(2, "one Add changeset, one Refresh changeset");
        results.Messages[1].Count.Should().Be(1);
        var refresh = results.Messages[1].First();
        refresh.Reason.Should().Be(ListChangeReason.Refresh);
        refresh.Item.CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void Remove_StopsReevaluatorFromFiringRefresh()
    {
        var source = new SourceList<Item>();
        var item = new Item();

        using var results = source.Connect()
            .AutoRefresh(x => x.Value)
            .AsAggregator();

        source.Add(item);
        source.Remove(item);
        item.Value = 99;

        results.Messages.SelectMany(cs => cs).Should().NotContain(c => c.Reason == ListChangeReason.Refresh);
    }

    [Fact]
    public void MultipleItems_EachGetsOwnSubscription()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();
        var c = new Item();

        using var results = source.Connect()
            .AutoRefresh(x => x.Value)
            .AsAggregator();

        source.AddRange(new[] { a, b, c });

        a.Value = 1;
        b.Value = 2;
        c.Value = 3;

        var refreshes = results.Messages.SelectMany(cs => cs).Where(c => c.Reason == ListChangeReason.Refresh).ToList();
        refreshes.Should().HaveCount(3);
        refreshes.Select(r => r.Item.CurrentIndex).Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    [Fact]
    public void Move_DoesNotReleaseSubscriptionAndIndexUpdates()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();
        var c = new Item();

        using var results = source.Connect()
            .AutoRefresh(x => x.Value)
            .AsAggregator();

        source.AddRange(new[] { a, b, c });

        // Move a from index 0 to index 2
        source.Move(0, 2);

        // Now reevaluator on a should fire a Refresh at index 2
        a.Value = 99;

        var refresh = results.Messages.SelectMany(cs => cs).Where(c => c.Reason == ListChangeReason.Refresh).ToList();
        refresh.Should().NotBeEmpty();
        refresh.Last().Item.CurrentIndex.Should().Be(2, "a moved to index 2, subscription persists, Refresh emitted at new index");
    }

    [Fact]
    public void Replace_NewItemGetsItsOwnSubscription_OldItemRefreshesIgnored()
    {
        var source = new SourceList<Item>();
        var oldItem = new Item();
        var newItem = new Item();

        using var results = source.Connect()
            .AutoRefresh(x => x.Value)
            .AsAggregator();

        source.Add(oldItem);
        source.Replace(oldItem, newItem);

        oldItem.Value = 100;
        newItem.Value = 200;

        var refreshes = results.Messages.SelectMany(cs => cs).Where(c => c.Reason == ListChangeReason.Refresh).ToList();
        refreshes.Should().HaveCount(1, "only newItem still has a live subscription");
        refreshes[0].Item.Current.Should().BeSameAs(newItem);
    }

    [Fact]
    public void Clear_ReleasesAllSubscriptions()
    {
        var source = new SourceList<Item>();
        var a = new Item();
        var b = new Item();

        using var results = source.Connect()
            .AutoRefresh(x => x.Value)
            .AsAggregator();

        source.AddRange(new[] { a, b });
        source.Clear();

        a.Value = 1;
        b.Value = 2;

        results.Messages.SelectMany(cs => cs).Should().NotContain(c => c.Reason == ListChangeReason.Refresh);
    }

    private sealed class Item : System.ComponentModel.INotifyPropertyChanged
    {
        private int _value;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public int Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
}
