using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using DynamicData.Binding;
using DynamicData.Tests.Domain;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.Cache;

public class AutoRefreshFixture
{
    [Fact]
    public void AutoRefresh()
    {
        var items = Enumerable.Range(1, 100).Select(i => new Person("Person" + i, 1)).ToArray();

        //result should only be true when all items are set to true
        using var cache = new SourceCache<Person, string>(m => m.Name);
        using var results = cache.Connect().AutoRefresh(p => p.Age).AsAggregator();
        cache.AddOrUpdate(items);

        results.Data.Count.Should().Be(100);
        results.Messages.Count.Should().Be(1);

        items[0].Age = 10;
        results.Data.Count.Should().Be(100);
        results.Messages.Count.Should().Be(2);

        results.Messages[1].First().Reason.Should().Be(ChangeReason.Refresh);

        //remove an item and check no change is fired
        var toRemove = items[1];
        cache.Remove(toRemove);
        results.Data.Count.Should().Be(99);
        results.Messages.Count.Should().Be(3);
        toRemove.Age = 100;
        results.Messages.Count.Should().Be(3);

        //add it back in and check it updates
        cache.AddOrUpdate(toRemove);
        results.Messages.Count.Should().Be(4);
        toRemove.Age = 101;
        results.Messages.Count.Should().Be(5);

        results.Messages.Last().First().Reason.Should().Be(ChangeReason.Refresh);
    }

    [Fact]
    public void AutoRefreshFromObservable()
    {
        var items = Enumerable.Range(1, 100).Select(i => new Person("Person" + i, 1)).ToArray();

        //result should only be true when all items are set to true
        using var cache = new SourceCache<Person, string>(m => m.Name);
        using var results = cache.Connect().AutoRefreshOnObservable(p => p.WhenAnyPropertyChanged()).AsAggregator();
        cache.AddOrUpdate(items);

        results.Data.Count.Should().Be(100);
        results.Messages.Count.Should().Be(1);

        items[0].Age = 10;
        results.Data.Count.Should().Be(100);
        results.Messages.Count.Should().Be(2);

        results.Messages[1].First().Reason.Should().Be(ChangeReason.Refresh);

        //remove an item and check no change is fired
        var toRemove = items[1];
        cache.Remove(toRemove);
        results.Data.Count.Should().Be(99);
        results.Messages.Count.Should().Be(3);
        toRemove.Age = 100;
        results.Messages.Count.Should().Be(3);

        //add it back in and check it updates
        cache.AddOrUpdate(toRemove);
        results.Messages.Count.Should().Be(4);
        toRemove.Age = 101;
        results.Messages.Count.Should().Be(5);

        results.Messages.Last().First().Reason.Should().Be(ChangeReason.Refresh);
    }

    [Fact]
    public void MakeSelectMagicWorkWithObservable()
    {
        var initialItem = new IntHolder(1, "Initial Description");

        var sourceList = new SourceList<IntHolder>();
        sourceList.Add(initialItem);

        var descriptionStream = sourceList.Connect().AutoRefresh(intHolder => intHolder!.Description).Transform(intHolder => intHolder!.Description, true).Do(x => { }) // <--- Add break point here to check the overload fixes it
            .Bind(out var resultCollection);

        using (descriptionStream.Subscribe())
        {
            var newDescription = "New Description";
            initialItem.Description = newDescription;

            newDescription.Should().Be(resultCollection[0]);
            //Assert.AreEqual(newDescription, resultCollection[0]);
        }
    }

    public class IntHolder(int value, string description) : AbstractNotifyPropertyChanged
    {
        public string _description_ = description;

        public int _value = value;

        public string Description
        {
            get => _description_;
            set => SetAndRaise(ref _description_, value);
        }

        public int Value
        {
            get => _value;
            set => SetAndRaise(ref _value, value);
        }
    }

    // Regression for #1099. When an item is added and removed within a single upstream
    // changeset and the reevaluator emits synchronously upon subscription, the operator
    // must not produce a Refresh for the now-removed item.
    [Fact]
    public void AutoRefreshOnObservable_AddAndRemoveInSameChangeSet_NoRefreshAfterRemove()
    {
        using var cache = new SourceCache<Person, string>(p => p.Name);
        var person = new Person("Bob", 1);

        using var results = cache.Connect()
            .AutoRefreshOnObservable(_ => Observable.Return(Unit.Default))
            .AsAggregator();

        cache.Edit(updater =>
        {
            updater.AddOrUpdate(person);
            updater.Remove(person);
        });

        results.Messages.Should().NotContain(
            cs => cs.Refreshes > 0,
            "an immediately-firing reevaluator must not produce a refresh for an item that no longer exists");
        results.Data.Count.Should().Be(0);
    }

    // The reevaluator's emission, once subscribed, still produces Refresh changes as expected.
    [Fact]
    public void AutoRefreshOnObservable_ReevaluatorEmits_RefreshPropagates()
    {
        using var cache = new SourceCache<Person, string>(p => p.Name);
        var person = new Person("Bob", 1);
        var trigger = new Subject<Unit>();

        using var results = cache.Connect()
            .AutoRefreshOnObservable(_ => trigger)
            .AsAggregator();

        cache.AddOrUpdate(person);

        var messagesAfterAdd = results.Messages.Count;

        trigger.OnNext(Unit.Default);

        results.Messages.Count.Should().Be(messagesAfterAdd + 1);
        var refresh = results.Messages[^1];
        refresh.Refreshes.Should().Be(1);
        refresh.First().Reason.Should().Be(ChangeReason.Refresh);
        refresh.First().Key.Should().Be("Bob");
    }
}
