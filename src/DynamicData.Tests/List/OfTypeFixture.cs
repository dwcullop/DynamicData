// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Linq;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class OfTypeFixture
{
    [Fact]
    public void OnlyMatchingTypesAreIncluded()
    {
        var source = new SourceList<Animal>();

        using var results = source.Connect()
            .OfType<Animal, Dog>()
            .AsAggregator();

        source.AddRange(new Animal[]
        {
            new Dog("Rex"),
            new Cat("Whiskers"),
            new Dog("Spot")
        });

        results.Data.Count.Should().Be(2);
        results.Data.Items.Select(d => d.Name).Should().BeEquivalentTo(new[] { "Rex", "Spot" });
    }

    [Fact]
    public void Remove_RemovesFromTypedResult()
    {
        var source = new SourceList<Animal>();
        var rex = new Dog("Rex");
        var spot = new Dog("Spot");

        using var results = source.Connect()
            .OfType<Animal, Dog>()
            .AsAggregator();

        source.AddRange(new Animal[] { rex, spot });
        source.Remove(rex);

        results.Data.Items.Should().BeEquivalentTo(new[] { spot });
    }

    [Fact]
    public void NullSource_Throws()
    {
        Action act = () => ObservableListEx.OfType<Animal, Dog>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private abstract record Animal(string Name);

    private sealed record Dog(string Name) : Animal(Name);

    private sealed record Cat(string Name) : Animal(Name);
}
