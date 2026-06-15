// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class TransformManyAsyncFixture
{
    [Fact]
    public async Task ChildChangeSets_MergedIntoOutput()
    {
        var source = new SourceList<Parent>();
        var parentA = new Parent("A");
        var parentB = new Parent("B");
        parentA.Children.AddRange(new[] { 1, 2 });
        parentB.Children.AddRange(new[] { 10, 20 });

        using var results = source.Connect()
            .TransformManyAsync(p => Task.FromResult(p.Children.Connect()))
            .AsAggregator();

        source.AddRange(new[] { parentA, parentB });

        // Allow the Task<IObservable<...>> to complete and the child changesets to flow through.
        await Task.Delay(50);

        results.Data.Items.Should().BeEquivalentTo(new[] { 1, 2, 10, 20 });
    }

    [Fact]
    public async Task ParentRemove_RemovesAllOfThatParentsChildItems()
    {
        var source = new SourceList<Parent>();
        var parentA = new Parent("A");
        var parentB = new Parent("B");
        parentA.Children.AddRange(new[] { 1, 2 });
        parentB.Children.AddRange(new[] { 10, 20 });

        using var results = source.Connect()
            .TransformManyAsync(p => Task.FromResult(p.Children.Connect()))
            .AsAggregator();

        source.AddRange(new[] { parentA, parentB });
        await Task.Delay(50);

        source.Remove(parentA);
        await Task.Delay(50);

        results.Data.Items.Should().BeEquivalentTo(new[] { 10, 20 });
    }

    [Fact]
    public async Task ChildMutation_UpdatesOutput()
    {
        var source = new SourceList<Parent>();
        var parent = new Parent("A");
        parent.Children.Add(1);

        using var results = source.Connect()
            .TransformManyAsync(p => Task.FromResult(p.Children.Connect()))
            .AsAggregator();

        source.Add(parent);
        await Task.Delay(50);

        parent.Children.Add(2);
        parent.Children.Add(3);
        await Task.Delay(50);

        results.Data.Items.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task DelayedAsyncResult_IntegratesWhenReady()
    {
        var source = new SourceList<Parent>();
        var parent = new Parent("A");
        parent.Children.AddRange(new[] { 1, 2 });

        using var results = source.Connect()
            .TransformManyAsync(p => Task.Run(async () =>
            {
                await Task.Delay(20);
                return p.Children.Connect();
            }))
            .AsAggregator();

        source.Add(parent);
        await Task.Delay(150);

        results.Data.Items.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void NullSourceOrTransformer_Throws()
    {
        var source = new SourceList<Parent>();

        Action act1 = () => ObservableListEx.TransformManyAsync<Parent, int>(null!, _ => Task.FromResult(Observable.Empty<IChangeSet<int>>()));
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => source.Connect().TransformManyAsync<Parent, int>(null!);
        act2.Should().Throw<ArgumentNullException>();
    }

    private sealed class Parent
    {
        public Parent(string name)
        {
            Name = name;
            Children = new SourceList<int>();
        }

        public string Name { get; }

        public SourceList<int> Children { get; }
    }
}
