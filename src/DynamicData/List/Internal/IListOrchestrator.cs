// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace DynamicData.List.Internal;

/// <summary>
/// Contract implemented by list operator authors. The orchestrator decides what to do on each
/// source changeset and each inner emission. The driver (<see cref="ListOrchestration{TSource,TInner,TResult}"/>)
/// handles subscription accounting, serialization, and disposal.
/// </summary>
/// <typeparam name="TSource">The source item type.</typeparam>
/// <typeparam name="TInner">The per-slot inner observable value type.</typeparam>
/// <typeparam name="TResult">The downstream result type.</typeparam>
internal interface IListOrchestrator<TSource, TInner, TResult>
    where TSource : notnull
{
    /// <summary>
    /// Called on each slot-bearing changeset from the source. Walk the changes and call
    /// <see cref="IListOrchestratorContext{TSource,TInner}.Track"/> /
    /// <see cref="IListOrchestratorContext{TSource,TInner}.Untrack"/> as needed.
    /// </summary>
    /// <param name="changes">The slot-bearing changeset from the source.</param>
    /// <param name="context">Driver-provided context for tracking inner subscriptions.</param>
    void OnSourceChangeSet(
        IChangeSet<IListSlot<TSource>> changes,
        IListOrchestratorContext<TSource, TInner> context);

    /// <summary>
    /// Called when a tracked inner observable emits a value. The associated slot identifies
    /// which source item produced the value.
    /// </summary>
    /// <param name="value">The value emitted by the inner observable.</param>
    /// <param name="slot">The slot for the source item that owns the inner subscription.</param>
    /// <param name="emitter">Downstream observer to which the orchestrator may emit results.</param>
    void OnInner(TInner value, IListSlot<TSource> slot, IObserver<TResult> emitter);

    /// <summary>
    /// Called after a drain cycle of the shared delivery queue finishes.
    /// <paramref name="isFinal"/> is <see langword="true"/> when the source and every tracked
    /// inner have terminated; orchestrators that defer emissions should flush synchronously
    /// in that case.
    /// </summary>
    /// <param name="isFinal">True when the source and every tracked inner have terminated.</param>
    /// <param name="emitter">Downstream observer to which the orchestrator may emit deferred results.</param>
    void OnDrainComplete(bool isFinal, IObserver<TResult> emitter);
}
