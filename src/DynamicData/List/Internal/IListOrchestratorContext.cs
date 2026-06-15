// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Driver-provided API surface for list orchestrators. Tracks per-slot inner subscriptions and
/// routes ad-hoc observables through the shared delivery queue.
/// </summary>
/// <typeparam name="TSource">The source item type.</typeparam>
/// <typeparam name="TInner">The per-slot inner observable value type.</typeparam>
internal interface IListOrchestratorContext<TSource, TInner>
    where TSource : notnull
{
    /// <summary>
    /// Subscribes the supplied observable on behalf of the given slot. Participates in
    /// completion accounting: downstream stays alive until every tracked inner has terminated.
    /// Calling <see cref="Track"/> on a slot that already has a tracked inner replaces the
    /// prior subscription.
    /// </summary>
    /// <param name="slot">The slot the inner subscription belongs to.</param>
    /// <param name="observable">The inner observable to subscribe.</param>
    void Track(IListSlot<TSource> slot, IObservable<TInner> observable);

    /// <summary>
    /// Disposes any inner subscription associated with the slot. No-op if the slot is not
    /// currently tracked.
    /// </summary>
    /// <param name="slot">The slot whose inner subscription should be released.</param>
    void Untrack(IListSlot<TSource> slot);

    /// <summary>
    /// Wraps the observable so its notifications flow through the same serialization gate as
    /// source and inner notifications. Does NOT participate in completion accounting; the
    /// downstream stream can complete while a Serialize-wrapped subscription is still active.
    /// </summary>
    /// <typeparam name="T">The wrapped observable's value type.</typeparam>
    /// <param name="observable">The observable to wrap.</param>
    /// <returns>An observable that delivers through the shared queue.</returns>
    IObservable<T> Serialize<T>(IObservable<T> observable);

    /// <summary>
    /// Defers an action to fire after any inner emissions that were enqueued during the current
    /// callback finish draining. Useful when an orchestrator needs to do cleanup AFTER a newly
    /// tracked observable has delivered its initial state (e.g. Replace semantics).
    /// </summary>
    /// <param name="action">The action to defer.</param>
    void DeferAction(Action action);
}
