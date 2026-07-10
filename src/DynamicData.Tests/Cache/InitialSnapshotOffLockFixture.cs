// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;

using Bogus;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.Cache;

/// <summary>
/// Verifies that <see cref="IObservableCache{TObject, TKey}.Connect"/> and
/// <see cref="IObservableCache{TObject, TKey}.Watch"/> deliver their initial snapshot WITHOUT
/// holding the cache lock, and that doing so misses or duplicates no changes when subscription
/// races concurrent edits.
/// </summary>
[Collection(CollectionName)]
public sealed class InitialSnapshotOffLockFixture
{
    public const string CollectionName = "InitialSnapshotOffLock";

    [Fact]
    public void ConnectInitialSnapshotIsDeliveredOffTheLock()
    {
        using var cache = new SourceCache<int, int>(static x => x);
        cache.AddOrUpdate(Enumerable.Range(0, 10));

        using var deliveryStarted = new ManualResetEventSlim(false);
        using var releaseDelivery = new ManualResetEventSlim(false);
        using var editDone = new ManualResetEventSlim(false);

        // Subscriber blocks on the connect thread while receiving its initial snapshot.
        var blockOnce = true;
        var connectThread = new Thread(() =>
            cache.Connect().Subscribe(_ =>
            {
                if (blockOnce)
                {
                    blockOnce = false;
                    deliveryStarted.Set();
                    releaseDelivery.Wait(TimeSpan.FromSeconds(30));
                }
            })) { IsBackground = true };
        connectThread.Start();
        deliveryStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the initial snapshot delivery should have started");

        // A concurrent edit must complete while the snapshot is blocked: the lock is not held.
        var editThread = new Thread(() => { cache.AddOrUpdate(99); editDone.Set(); }) { IsBackground = true };
        editThread.Start();
        var editCompleted = editDone.Wait(TimeSpan.FromSeconds(10));

        releaseDelivery.Set();
        connectThread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("connect should complete");
        editThread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("edit should complete");

        editCompleted.Should().BeTrue("a concurrent edit must not block while the Connect initial snapshot is delivered; the cache lock must not be held during delivery");
    }

    [Fact]
    public void WatchInitialValueIsDeliveredOffTheLock()
    {
        using var cache = new SourceCache<int, int>(static x => x);
        cache.AddOrUpdate(5);

        using var deliveryStarted = new ManualResetEventSlim(false);
        using var releaseDelivery = new ManualResetEventSlim(false);
        using var editDone = new ManualResetEventSlim(false);

        var blockOnce = true;
        var watchThread = new Thread(() =>
            cache.Watch(5).Subscribe(_ =>
            {
                if (blockOnce)
                {
                    blockOnce = false;
                    deliveryStarted.Set();
                    releaseDelivery.Wait(TimeSpan.FromSeconds(30));
                }
            })) { IsBackground = true };
        watchThread.Start();
        deliveryStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the watch initial value delivery should have started");

        var editThread = new Thread(() => { cache.AddOrUpdate(99); editDone.Set(); }) { IsBackground = true };
        editThread.Start();
        var editCompleted = editDone.Wait(TimeSpan.FromSeconds(10));

        releaseDelivery.Set();
        watchThread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("watch should complete");
        editThread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("edit should complete");

        editCompleted.Should().BeTrue("a concurrent edit must not block while the Watch initial value is delivered; the cache lock must not be held during delivery");
    }

    [Fact]
    public async Task ConnectRacingConcurrentEditsConvergesWithNoMissedOrDuplicatedChanges()
    {
        using var cache = new SourceCache<int, int>(static x => x);
        cache.AddOrUpdate(Enumerable.Range(0, 100));

        var published = cache.Connect().Publish();
        var completion = published.LastOrDefaultAsync().ToTask();
        using var results = published.AsAggregator();
        using var connection = published.Connect();

        // Four threads mutate the cache while the connection is live; the connection must
        // observe every net change exactly once and converge to the final cache state.
        var editors = Enumerable.Range(0, 4).Select(thread => Task.Run(() =>
        {
            var randomizer = new Randomizer(thread + 1);
            for (var i = 0; i < 1000; i++)
            {
                var key = randomizer.Int(0, 200);
                if (i % 4 == 0)
                {
                    cache.RemoveKey(key);
                }
                else
                {
                    cache.AddOrUpdate(key);
                }
            }
        })).ToArray();

        await Task.WhenAll(editors);

        var expected = cache.Items.OrderBy(static x => x).ToList();

        cache.Dispose();
        await completion;

        results.Data.Items.OrderBy(static x => x).Should().Equal(expected, "the connection must converge to the exact final cache state with no missed or duplicated changes");
        results.Error.Should().BeNull();
    }
}

[CollectionDefinition(InitialSnapshotOffLockFixture.CollectionName, DisableParallelization = true)]
public sealed class InitialSnapshotOffLockCollection;
