// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Linq;

using DynamicData.Tests.Domain;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class MergeManyCacheChangeSetsOrchestratorFixture
{
    [Fact]
    public void ParentRemove_RemovesAllOfRemovedItemsChildEntries()
    {
        var parent = new SourceList<Market>();
        var marketA = new Market(0);
        var marketB = new Market(1);
        marketA.AddUniquePrices(section: 0, count: 3, stride: 100, () => 1.0m);
        marketB.AddUniquePrices(section: 1, count: 3, stride: 100, () => 2.0m);

        using var results = parent.Connect()
            .MergeManyChangeSets(m => m.LatestPrices, MarketPrice.EqualityComparer)
            .AsAggregator();

        parent.AddRange(new[] { marketA, marketB });
        results.Data.Count.Should().Be(6);

        parent.Remove(marketA);
        results.Data.Count.Should().Be(3, "marketA's three prices should be removed");
    }

    [Fact]
    public void ParentReplace_WithOverlappingKeysAndComparer_UpdatesOnlyOnce()
    {
        // Validates the DeferAction-based Replace handling: the new slot's initial state
        // updates shared keys, and the deferred RemoveItems for the old slot becomes a no-op.
        // Without the defer, intermediate updates would propagate (15 updates instead of 10).
        var parent = new SourceList<Market>();
        var marketOriginal = new Market(0);
        var marketLow = new Market(1);
        var marketLowLow = new Market(2);
        marketOriginal.SetPrices(0, 5, () => 100m);
        marketLow.SetPrices(0, 5, () => 50m);
        marketLowLow.SetPrices(0, 5, () => 25m);

        using var results = parent.Connect()
            .MergeManyChangeSets(m => m.LatestPrices, MarketPrice.LowPriceCompare)
            .AsAggregator();

        parent.Add(marketOriginal);
        parent.Add(marketLow);
        parent.Replace(marketLow, marketLowLow);

        results.Summary.Overall.Adds.Should().Be(5);
        results.Summary.Overall.Updates.Should().Be(10);
        results.Summary.Overall.Removes.Should().Be(0);
        results.Data.Count.Should().Be(5);
    }

    [Fact]
    public void ParentReplace_WithNonOverlappingKeys_RemovesOldAndAddsNew()
    {
        var parent = new SourceList<Market>();
        var marketOriginal = new Market(0);
        var marketReplacement = new Market(1);
        marketOriginal.SetPrices(0, 5, () => 100m);
        marketReplacement.SetPrices(10, 15, () => 50m);

        using var results = parent.Connect()
            .MergeManyChangeSets(m => m.LatestPrices, MarketPrice.EqualityComparer)
            .AsAggregator();

        parent.Add(marketOriginal);
        parent.Replace(marketOriginal, marketReplacement);

        results.Data.Count.Should().Be(5);
        results.Data.Items.Select(p => p.MarketId).Should().AllBeEquivalentTo(marketReplacement.Id);
    }

    [Fact]
    public void Clear_RemovesAllItemsFromMergedResult()
    {
        var parent = new SourceList<Market>();
        var marketA = new Market(0);
        var marketB = new Market(1);
        marketA.AddUniquePrices(0, 3, 100, () => 1.0m);
        marketB.AddUniquePrices(1, 3, 100, () => 2.0m);

        using var results = parent.Connect()
            .MergeManyChangeSets(m => m.LatestPrices, MarketPrice.EqualityComparer)
            .AsAggregator();

        parent.AddRange(new[] { marketA, marketB });
        parent.Clear();

        results.Data.Count.Should().Be(0);
    }
}
