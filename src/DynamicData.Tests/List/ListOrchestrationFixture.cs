// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;

using DynamicData.Kernel;
using DynamicData.List.Internal;
using DynamicData.Tests.Utilities;

using FluentAssertions;

using Xunit;

namespace DynamicData.Tests.List;

public sealed class ListOrchestrationFixture
{
    [Fact]
    public void SourceChangeSets_AreRoutedToOrchestrator()
    {
        using var source = new Subject<IChangeSet<string>>();
        RecordingListOrchestrator<string, int, int>? orch = null;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
            {
                orch = new RecordingListOrchestrator<string, int, int>();
                return orch;
            })
            .Subscribe();

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "b", 1) });

        orch.Should().NotBeNull();
        orch!.SourceChangeSets.Should().HaveCount(2);
    }

    [Fact]
    public void Track_RoutesInnerEmissionsThroughOnInner()
    {
        using var source = new Subject<IChangeSet<string>>();
        using var inner = new Subject<int>();
        RecordingListOrchestrator<string, int, int>? orch = null;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
            {
                orch = new RecordingListOrchestrator<string, int, int>
                {
                    OnSourceChangeSetHook = (changes, c) =>
                    {
                        foreach (var change in changes)
                        {
                            if (change.Reason == ListChangeReason.Add)
                            {
                                c.Track(change.Item.Current, inner);
                            }
                        }
                    }
                };
                return orch;
            })
            .Subscribe();

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        inner.OnNext(42);
        inner.OnNext(100);

        orch.Should().NotBeNull();
        orch!.Inners.Should().HaveCount(2);
        orch.Inners[0].Value.Should().Be(42);
        orch.Inners[0].Slot.Item.Should().Be("a");
        orch.Inners[1].Value.Should().Be(100);
    }

    [Fact]
    public void Untrack_DisposesInnerSubscription()
    {
        using var source = new Subject<IChangeSet<string>>();
        using var inner = new Subject<int>();
        RecordingListOrchestrator<string, int, int>? orch = null;
        IListSlot<string>? trackedSlot = null;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
            {
                orch = new RecordingListOrchestrator<string, int, int>
                {
                    OnSourceChangeSetHook = (changes, c) =>
                    {
                        foreach (var change in changes)
                        {
                            if (change.Reason == ListChangeReason.Add)
                            {
                                trackedSlot = change.Item.Current;
                                c.Track(trackedSlot, inner);
                            }
                            else if (change.Reason == ListChangeReason.Remove)
                            {
                                c.Untrack(change.Item.Current);
                            }
                        }
                    }
                };
                return orch;
            })
            .Subscribe();

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        inner.OnNext(1);
        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Remove, "a", 0) });
        inner.OnNext(2);
        inner.OnNext(3);

        orch.Should().NotBeNull();
        orch!.Inners.Should().HaveCount(1, "only the pre-Untrack emission should reach OnInner");
        orch.Inners[0].Value.Should().Be(1);
    }

    [Fact]
    public void SourceCompleted_WithNoInners_FiresDownstreamOnCompleted()
    {
        using var source = new Subject<IChangeSet<string>>();
        var completed = false;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
                new RecordingListOrchestrator<string, int, int>())
            .Subscribe(_ => { }, _ => { }, () => completed = true);

        source.OnCompleted();

        completed.Should().BeTrue();
    }

    [Fact]
    public void SourceCompleted_WithLiveInners_DefersOnCompletedUntilInnersComplete()
    {
        using var source = new Subject<IChangeSet<string>>();
        using var inner = new Subject<int>();
        var completed = false;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
                new RecordingListOrchestrator<string, int, int>
                {
                    OnSourceChangeSetHook = (changes, c) =>
                    {
                        foreach (var change in changes)
                        {
                            if (change.Reason == ListChangeReason.Add)
                            {
                                c.Track(change.Item.Current, inner);
                            }
                        }
                    }
                })
            .Subscribe(_ => { }, _ => { }, () => completed = true);

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        source.OnCompleted();

        completed.Should().BeFalse("inner still alive");

        inner.OnCompleted();

        completed.Should().BeTrue("inner completion brought subscription count to zero");
    }

    [Fact]
    public void SourceError_PropagatesToDownstream()
    {
        using var source = new Subject<IChangeSet<string>>();
        Exception? receivedError = null;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
                new RecordingListOrchestrator<string, int, int>())
            .Subscribe(_ => { }, ex => receivedError = ex);

        var boom = new Exception("source failed");
        source.OnError(boom);

        receivedError.Should().BeSameAs(boom);
    }

    [Fact]
    public void OrchestratorThrowsInOnSourceChangeSet_PropagatesAsOnError()
    {
        using var source = new Subject<IChangeSet<string>>();
        Exception? receivedError = null;
        var boom = new InvalidOperationException("orchestrator failed");

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
                new RecordingListOrchestrator<string, int, int>
                {
                    OnSourceChangeSetHook = (_, _) => throw boom
                })
            .Subscribe(_ => { }, ex => receivedError = ex);

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });

        receivedError.Should().BeSameAs(boom);
    }

    [Fact]
    public void OnDrainComplete_FiresWithIsFinalTrueAfterFullTermination()
    {
        using var source = new Subject<IChangeSet<string>>();
        RecordingListOrchestrator<string, int, int>? orch = null;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
            {
                orch = new RecordingListOrchestrator<string, int, int>();
                return orch;
            })
            .Subscribe();

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        source.OnCompleted();

        orch.Should().NotBeNull();
        orch!.DrainCompletes.Should().NotBeEmpty();
        orch.DrainCompletes.Last().IsFinal.Should().BeTrue("source completed with no live inners");
    }

    [Fact]
    public void Serialize_RoutesObservableThroughGateWithoutCompletionAccounting()
    {
        using var source = new Subject<IChangeSet<string>>();
        using var auxiliary = new Subject<int>();
        var auxValues = new List<int>();
        var completed = false;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
                new RecordingListOrchestrator<string, int, int>
                {
                    OnSourceChangeSetHook = (changes, c) =>
                    {
                        foreach (var change in changes)
                        {
                            if (change.Reason == ListChangeReason.Add)
                            {
                                c.Serialize(auxiliary).Subscribe(v => auxValues.Add(v));
                            }
                        }
                    }
                })
            .Subscribe(_ => { }, _ => { }, () => completed = true);

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        auxiliary.OnNext(1);
        auxiliary.OnNext(2);

        source.OnCompleted();

        completed.Should().BeTrue("Serialize does not participate in completion accounting; source completion alone completes downstream");
        auxValues.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void Track_OnSameSlotTwice_ReplacesInnerSubscription()
    {
        using var source = new Subject<IChangeSet<string>>();
        using var first = new Subject<int>();
        using var second = new Subject<int>();
        RecordingListOrchestrator<string, int, int>? orch = null;
        IListSlot<string>? slot = null;
        var calls = 0;

        using var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
            {
                orch = new RecordingListOrchestrator<string, int, int>
                {
                    OnSourceChangeSetHook = (changes, c) =>
                    {
                        foreach (var change in changes)
                        {
                            if (change.Reason == ListChangeReason.Add)
                            {
                                slot = change.Item.Current;
                                calls++;
                                c.Track(slot, calls == 1 ? first : second);
                            }
                            else if (change.Reason == ListChangeReason.Refresh)
                            {
                                calls++;
                                c.Track(slot!, second);
                            }
                        }
                    }
                };
                return orch;
            })
            .Subscribe();

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        first.OnNext(10);
        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Refresh, "a", Optional<string>.None, 0) });
        first.OnNext(20);
        second.OnNext(30);

        orch.Should().NotBeNull();
        orch!.Inners.Select(i => i.Value).Should().BeEquivalentTo(new[] { 10, 30 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Disposal_StopsAllCallbacks()
    {
        using var source = new Subject<IChangeSet<string>>();
        using var inner = new Subject<int>();
        RecordingListOrchestrator<string, int, int>? orch = null;

        var sub = source
            .Orchestrate<string, int, int>((ctx, observer) =>
            {
                orch = new RecordingListOrchestrator<string, int, int>
                {
                    OnSourceChangeSetHook = (changes, c) =>
                    {
                        foreach (var change in changes)
                        {
                            if (change.Reason == ListChangeReason.Add)
                            {
                                c.Track(change.Item.Current, inner);
                            }
                        }
                    }
                };
                return orch;
            })
            .Subscribe();

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", 0) });
        inner.OnNext(1);

        sub.Dispose();

        inner.OnNext(2);
        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "b", 1) });

        orch.Should().NotBeNull();
        orch!.Inners.Should().HaveCount(1, "no callbacks should fire after disposal");
        orch.SourceChangeSets.Should().HaveCount(1);
    }

    [Fact]
    public void ConstructionError_RollsBackResources()
    {
        using var source = new Subject<IChangeSet<string>>();
        var boom = new InvalidOperationException("factory failed");

        Action act = () => source
            .Orchestrate<string, int, int>((ctx, observer) => throw boom)
            .Subscribe();

        act.Should().Throw<InvalidOperationException>().WithMessage("factory failed");

        // If we got here without leaking resources, the test passes. There's no externally
        // observable handle to the leaked queue, so this test mainly proves the throw escapes
        // cleanly without crashing the runtime.
    }
}
