// Copyright (c) 2011-2025 Roland Pheasant. All rights reserved.
// Roland Pheasant licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

using DynamicData.List.Internal;

namespace DynamicData.Tests.Utilities;

/// <summary>
/// Recording orchestrator used for unit-testing <see cref="ListOrchestration{TSource,TInner,TResult}"/>
/// in isolation. Captures every callback the driver invokes and exposes hooks for tests to drive
/// downstream emissions or trigger errors.
/// </summary>
internal sealed class RecordingListOrchestrator<TSource, TInner, TResult>
    : IListOrchestrator<TSource, TInner, TResult>
    where TSource : notnull
{
    public List<IChangeSet<IListSlot<TSource>>> SourceChangeSets { get; } = new();

    public List<InnerEmission> Inners { get; } = new();

    public List<DrainCompleteCall> DrainCompletes { get; } = new();

    public Action<IChangeSet<IListSlot<TSource>>, IListOrchestratorContext<TSource, TInner>>? OnSourceChangeSetHook { get; set; }

    public Action<TInner, IListSlot<TSource>, IObserver<TResult>>? OnInnerHook { get; set; }

    public Action<bool, IObserver<TResult>>? OnDrainCompleteHook { get; set; }

    public void OnSourceChangeSet(IChangeSet<IListSlot<TSource>> changes, IListOrchestratorContext<TSource, TInner> context)
    {
        SourceChangeSets.Add(changes);
        OnSourceChangeSetHook?.Invoke(changes, context);
    }

    public void OnInner(TInner value, IListSlot<TSource> slot, IObserver<TResult> emitter)
    {
        Inners.Add(new InnerEmission(value, slot));
        OnInnerHook?.Invoke(value, slot, emitter);
    }

    public void OnDrainComplete(bool isFinal, IObserver<TResult> emitter)
    {
        DrainCompletes.Add(new DrainCompleteCall(isFinal));
        OnDrainCompleteHook?.Invoke(isFinal, emitter);
    }

    public sealed record InnerEmission(TInner Value, IListSlot<TSource> Slot);

    public sealed record DrainCompleteCall(bool IsFinal);
}
