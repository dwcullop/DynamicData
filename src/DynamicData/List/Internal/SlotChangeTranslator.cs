// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Linq;

namespace DynamicData.List.Internal;

/// <summary>
/// Translates a slot-bearing change back into an item-bearing change, used by orchestrators
/// that wrap a list source with slots internally but emit unwrapped <see cref="IChangeSet{T}"/>
/// downstream.
/// </summary>
internal static class SlotChangeTranslator
{
    public static Change<T> Unwrap<T>(this Change<IListSlot<T>> slotChange)
        where T : notnull => slotChange.Reason switch
    {
        ListChangeReason.Add => new Change<T>(ListChangeReason.Add, slotChange.Item.Current.Item, slotChange.Item.CurrentIndex),

        ListChangeReason.AddRange => new Change<T>(ListChangeReason.AddRange, slotChange.Range.Select(s => s.Item).ToList(), slotChange.Range.Index),

        ListChangeReason.Replace => new Change<T>(
            ListChangeReason.Replace,
            slotChange.Item.Current.Item,
            slotChange.Item.Previous.HasValue ? Optional.Some(slotChange.Item.Previous.Value.Item) : Optional<T>.None,
            slotChange.Item.CurrentIndex,
            slotChange.Item.PreviousIndex),

        ListChangeReason.Remove => new Change<T>(ListChangeReason.Remove, slotChange.Item.Current.Item, slotChange.Item.CurrentIndex),

        ListChangeReason.RemoveRange => new Change<T>(ListChangeReason.RemoveRange, slotChange.Range.Select(s => s.Item).ToList(), slotChange.Range.Index),

        ListChangeReason.Clear => new Change<T>(ListChangeReason.Clear, slotChange.Range.Select(s => s.Item).ToList()),

        ListChangeReason.Refresh => new Change<T>(ListChangeReason.Refresh, slotChange.Item.Current.Item, Optional<T>.None, slotChange.Item.CurrentIndex),

        ListChangeReason.Moved => new Change<T>(slotChange.Item.Current.Item, slotChange.Item.CurrentIndex, slotChange.Item.PreviousIndex),

        _ => throw new ArgumentOutOfRangeException(nameof(slotChange), slotChange.Reason, "Unsupported list change reason")
    };
}
