// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Reactive.Linq;

namespace DynamicData.List.Internal;

/// <summary>
/// Internal extension entry points for list orchestrators. Public API is unchanged; these
/// helpers exist to let internal operator implementations reuse the orchestrator pattern.
/// </summary>
internal static class IntObservableListEx
{
    /// <summary>
    /// Build an orchestrator-driven operator from a per-subscription factory. The factory is
    /// invoked once per downstream subscription, isolating all orchestrator state.
    /// </summary>
    /// <typeparam name="TSource">Source list item type.</typeparam>
    /// <typeparam name="TInner">Per-slot inner observable value type.</typeparam>
    /// <typeparam name="TResult">Downstream result type.</typeparam>
    /// <param name="source">Source observable list changeset.</param>
    /// <param name="orchestratorFactory">Factory that builds the orchestrator instance, given a context and a downstream observer.</param>
    /// <returns>An observable of <typeparamref name="TResult"/> driven by the orchestrator.</returns>
    public static IObservable<TResult> Orchestrate<TSource, TInner, TResult>(
        this IObservable<IChangeSet<TSource>> source,
        Func<IListOrchestratorContext<TSource, TInner>, IObserver<TResult>, IListOrchestrator<TSource, TInner, TResult>> orchestratorFactory)
        where TSource : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        orchestratorFactory.ThrowArgumentNullExceptionIfNull(nameof(orchestratorFactory));

        return Observable.Create<TResult>(observer =>
            new ListOrchestration<TSource, TInner, TResult>(source, orchestratorFactory, observer));
    }

    /// <summary>
    /// Build an orchestrator-driven operator from a lambda triple. Convenience overload for
    /// stateless orchestrators that don't need a class to hold state.
    /// </summary>
    /// <typeparam name="TSource">Source list item type.</typeparam>
    /// <typeparam name="TInner">Per-slot inner observable value type.</typeparam>
    /// <typeparam name="TResult">Downstream result type.</typeparam>
    /// <param name="source">Source observable list changeset.</param>
    /// <param name="onSourceChangeSet">Handler for slot-bearing source changesets.</param>
    /// <param name="onInner">Handler for tracked inner observable emissions.</param>
    /// <param name="onDrainComplete">Optional handler for end-of-drain notifications.</param>
    /// <returns>An observable of <typeparamref name="TResult"/> driven by the lambda orchestrator.</returns>
    public static IObservable<TResult> Orchestrate<TSource, TInner, TResult>(
        this IObservable<IChangeSet<TSource>> source,
        Action<IChangeSet<IListSlot<TSource>>, IListOrchestratorContext<TSource, TInner>> onSourceChangeSet,
        Action<TInner, IListSlot<TSource>, IObserver<TResult>> onInner,
        Action<bool, IObserver<TResult>>? onDrainComplete = null)
        where TSource : notnull
    {
        source.ThrowArgumentNullExceptionIfNull(nameof(source));
        onSourceChangeSet.ThrowArgumentNullExceptionIfNull(nameof(onSourceChangeSet));
        onInner.ThrowArgumentNullExceptionIfNull(nameof(onInner));

        return source.Orchestrate<TSource, TInner, TResult>(
            (ctx, observer) => new LambdaOrchestrator<TSource, TInner, TResult>(
                onSourceChangeSet, onInner, onDrainComplete));
    }

    private sealed class LambdaOrchestrator<TSource, TInner, TResult> : IListOrchestrator<TSource, TInner, TResult>
        where TSource : notnull
    {
        private readonly Action<IChangeSet<IListSlot<TSource>>, IListOrchestratorContext<TSource, TInner>> _onSourceChangeSet;
        private readonly Action<TInner, IListSlot<TSource>, IObserver<TResult>> _onInner;
        private readonly Action<bool, IObserver<TResult>>? _onDrainComplete;

        public LambdaOrchestrator(
            Action<IChangeSet<IListSlot<TSource>>, IListOrchestratorContext<TSource, TInner>> onSourceChangeSet,
            Action<TInner, IListSlot<TSource>, IObserver<TResult>> onInner,
            Action<bool, IObserver<TResult>>? onDrainComplete)
        {
            _onSourceChangeSet = onSourceChangeSet;
            _onInner = onInner;
            _onDrainComplete = onDrainComplete;
        }

        public void OnSourceChangeSet(IChangeSet<IListSlot<TSource>> changes, IListOrchestratorContext<TSource, TInner> context) =>
            _onSourceChangeSet(changes, context);

        public void OnInner(TInner value, IListSlot<TSource> slot, IObserver<TResult> emitter) =>
            _onInner(value, slot, emitter);

        public void OnDrainComplete(bool isFinal, IObserver<TResult> emitter) =>
            _onDrainComplete?.Invoke(isFinal, emitter);
    }
}
