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

public sealed class SlotAllocatorFixture
{
    [Fact]
    public void SingleAdd_AllocatesSlotAtIndex()
    {
        using var source = new Subject<IChangeSet<string>>();
        var slots = new List<IListSlot<string>>();

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) slots.Add(c.Item.Current);
            }
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", index: 0) });

        slots.Should().HaveCount(1);
        slots[0].Item.Should().Be("a");
        slots[0].CurrentIndex.Should().Be(0);
        slots[0].IsReleased.Should().BeFalse();
    }

    [Fact]
    public void Add_ShiftsLaterSlots()
    {
        using var source = new Subject<IChangeSet<string>>();
        var allSlots = new List<IListSlot<string>>();

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) allSlots.Add(c.Item.Current);
            }
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", index: 0) });
        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "b", index: 0) });

        allSlots[0].Item.Should().Be("a");
        allSlots[0].CurrentIndex.Should().Be(1, "a was pushed to index 1 by b inserting at 0");
        allSlots[1].Item.Should().Be("b");
        allSlots[1].CurrentIndex.Should().Be(0);
    }

    [Fact]
    public void Remove_ReleasesSlotAndShiftsLaterSlots()
    {
        using var source = new Subject<IChangeSet<string>>();
        var allSlots = new List<IListSlot<string>>();

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) allSlots.Add(c.Item.Current);
            }
        });

        source.OnNext(new ChangeSet<string>
        {
            new(ListChangeReason.Add, "a", index: 0),
            new(ListChangeReason.Add, "b", index: 1),
            new(ListChangeReason.Add, "c", index: 2)
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Remove, "b", index: 1) });

        allSlots[0].IsReleased.Should().BeFalse();
        allSlots[1].IsReleased.Should().BeTrue("b was removed");
        allSlots[1].CurrentIndex.Should().Be(-1);
        allSlots[2].IsReleased.Should().BeFalse();
        allSlots[2].CurrentIndex.Should().Be(1, "c shifted down from 2 to 1");
    }

    [Fact]
    public void AddRange_AllocatesContiguousSlots()
    {
        using var source = new Subject<IChangeSet<string>>();
        var addRangeChange = (Change<IListSlot<string>>?)null;

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.AddRange) addRangeChange = c;
            }
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.AddRange, new[] { "a", "b", "c" }, index: 0) });

        addRangeChange.Should().NotBeNull();
        var ranged = addRangeChange!.Range.ToList();
        ranged.Should().HaveCount(3);
        ranged[0].Item.Should().Be("a");
        ranged[0].CurrentIndex.Should().Be(0);
        ranged[1].Item.Should().Be("b");
        ranged[1].CurrentIndex.Should().Be(1);
        ranged[2].Item.Should().Be("c");
        ranged[2].CurrentIndex.Should().Be(2);
    }

    [Fact]
    public void Replace_ReleasesOldAndAllocatesNew()
    {
        using var source = new Subject<IChangeSet<string>>();
        IListSlot<string>? originalSlot = null;
        IListSlot<string>? replacementSlot = null;

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) originalSlot = c.Item.Current;
                if (c.Reason == ListChangeReason.Replace) replacementSlot = c.Item.Current;
            }
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", index: 0) });
        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Replace, "b", Optional.Some("a"), currentIndex: 0, previousIndex: 0) });

        originalSlot.Should().NotBeNull();
        originalSlot!.IsReleased.Should().BeTrue();
        originalSlot.Item.Should().Be("a");

        replacementSlot.Should().NotBeNull();
        replacementSlot!.IsReleased.Should().BeFalse();
        replacementSlot.Item.Should().Be("b");
        replacementSlot.CurrentIndex.Should().Be(0);

        ReferenceEquals(originalSlot, replacementSlot).Should().BeFalse("Replace produces a new slot");
    }

    [Fact]
    public void Move_UpdatesIndicesWithoutReleasingSlots()
    {
        using var source = new Subject<IChangeSet<string>>();
        var slots = new List<IListSlot<string>>();

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) slots.Add(c.Item.Current);
            }
        });

        source.OnNext(new ChangeSet<string>
        {
            new(ListChangeReason.Add, "a", index: 0),
            new(ListChangeReason.Add, "b", index: 1),
            new(ListChangeReason.Add, "c", index: 2)
        });

        // Move "a" (index 0) to index 2
        source.OnNext(new ChangeSet<string> { new("a", currentIndex: 2, previousIndex: 0) });

        slots[0].IsReleased.Should().BeFalse("Move never releases slots");
        slots[1].IsReleased.Should().BeFalse();
        slots[2].IsReleased.Should().BeFalse();

        slots[0].Item.Should().Be("a");
        slots[0].CurrentIndex.Should().Be(2);
        slots[1].Item.Should().Be("b");
        slots[1].CurrentIndex.Should().Be(0);
        slots[2].Item.Should().Be("c");
        slots[2].CurrentIndex.Should().Be(1);
    }

    [Fact]
    public void Clear_ReleasesAllSlots()
    {
        using var source = new Subject<IChangeSet<string>>();
        var slots = new List<IListSlot<string>>();

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) slots.Add(c.Item.Current);
            }
        });

        source.OnNext(new ChangeSet<string>
        {
            new(ListChangeReason.Add, "a", index: 0),
            new(ListChangeReason.Add, "b", index: 1)
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Clear, new[] { "a", "b" }) });

        slots.Should().AllSatisfy(s => s.IsReleased.Should().BeTrue());
        slots.Should().AllSatisfy(s => s.CurrentIndex.Should().Be(-1));
    }

    [Fact]
    public void Refresh_DoesNotAllocateOrRelease()
    {
        using var source = new Subject<IChangeSet<string>>();
        var slots = new List<IListSlot<string>>();
        var refreshSlot = (IListSlot<string>?)null;

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) slots.Add(c.Item.Current);
                if (c.Reason == ListChangeReason.Refresh) refreshSlot = c.Item.Current;
            }
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", index: 0) });
        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Refresh, "a", Optional<string>.None, currentIndex: 0) });

        refreshSlot.Should().NotBeNull();
        ReferenceEquals(refreshSlot, slots[0]).Should().BeTrue("Refresh emits the existing slot");
        refreshSlot!.IsReleased.Should().BeFalse();
    }

    [Fact]
    public void Released_FiresOnceOnRemove()
    {
        using var source = new Subject<IChangeSet<string>>();
        var slots = new List<IListSlot<string>>();

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) slots.Add(c.Item.Current);
            }
        });

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Add, "a", index: 0) });

        var releaseCount = 0;
        using var releaseSub = slots[0].Released.Subscribe(_ => releaseCount++);

        source.OnNext(new ChangeSet<string> { new(ListChangeReason.Remove, "a", index: 0) });

        releaseCount.Should().Be(1);
    }

    [Fact]
    public void DuplicateItems_GetDistinctSlots()
    {
        // Lists allow duplicates; each Add allocates a fresh slot, even for equal items.
        using var source = new Subject<IChangeSet<string>>();
        var slots = new List<IListSlot<string>>();

        using var sub = new SlotAllocator<string>(source).Run().Subscribe(cs =>
        {
            foreach (var c in cs)
            {
                if (c.Reason == ListChangeReason.Add) slots.Add(c.Item.Current);
            }
        });

        source.OnNext(new ChangeSet<string>
        {
            new(ListChangeReason.Add, "x", index: 0),
            new(ListChangeReason.Add, "x", index: 1)
        });

        ReferenceEquals(slots[0], slots[1]).Should().BeFalse("each Add allocates a distinct slot");
        slots[0].Item.Should().Be("x");
        slots[1].Item.Should().Be("x");
        slots[0].Equals(slots[1]).Should().BeFalse("slot equality is reference identity");
    }
}
