using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;

namespace DynamicData.Tests.RxContract;

/// <summary>
/// Records what an operator emitted while it was being driven.
/// </summary>
internal sealed class ContractTrace
{
    /// <summary>
    /// Gets or sets the number of values received before any terminal event.
    /// </summary>
    public int Next { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the operator completed.
    /// </summary>
    public bool Completed { get; set; }

    /// <summary>
    /// Gets or sets the error the operator delivered, if any.
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// Gets or sets the exception the operator threw out of subscription, if any.
    /// </summary>
    public Exception? Threw { get; set; }

    /// <summary>
    /// Gets or sets the number of values received after a terminal event.
    /// </summary>
    public int AfterTerminal { get; set; }
}

/// <summary>
/// A source whose terminal behaviour is dictated by the audit rather than by the operator under test.
/// </summary>
internal sealed class ContractSource
{
    /// <summary>
    /// Gets the observable handed to the operator.
    /// </summary>
    public required object Observable { get; init; }

    /// <summary>
    /// Gets the action which pushes one value.
    /// </summary>
    public required Action<object> Push { get; init; }

    /// <summary>
    /// Gets the action which completes the source.
    /// </summary>
    public required Action Complete { get; init; }

    /// <summary>
    /// Creates a source which replays a script the moment a subscriber arrives.
    /// </summary>
    /// <param name="element">The element type carried by the stream.</param>
    /// <param name="script">The values and terminal event to replay.</param>
    /// <returns>The source.</returns>
    /// <remarks>
    /// Delivering terminal events during subscription matters. Several operators only misbehave when the
    /// terminal event arrives synchronously, which is exactly how <c>Switch</c> lost completion.
    /// </remarks>
    public static ContractSource Scripted(Type element, object[] script) =>
        (ContractSource)typeof(ContractSource).GetMethod(nameof(ScriptedCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(element).Invoke(null, [script])!;

    /// <summary>
    /// Creates a source driven from the outside, used for the disposal probe.
    /// </summary>
    /// <param name="element">The element type carried by the stream.</param>
    /// <returns>The source.</returns>
    public static ContractSource Live(Type element) =>
        (ContractSource)typeof(ContractSource).GetMethod(nameof(LiveCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(element).Invoke(null, [])!;

    /// <summary>
    /// Subscribes to an operator result and records the notifications.
    /// </summary>
    /// <param name="element">The element type carried by the result.</param>
    /// <param name="observable">The operator result.</param>
    /// <param name="trace">The trace to populate.</param>
    /// <returns>The subscription.</returns>
    public static IDisposable Watch(Type element, object observable, ContractTrace trace) =>
        (IDisposable)typeof(ContractSource).GetMethod(nameof(WatchCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(element).Invoke(null, [observable, trace])!;

    private static ContractSource ScriptedCore<T>(object[] script) => new()
    {
        Observable = System.Reactive.Linq.Observable.Create<T>(observer =>
        {
            foreach (var step in script)
            {
                switch (step)
                {
                    case Exception error:
                        observer.OnError(error);
                        break;

                    case string:
                        observer.OnCompleted();
                        break;

                    default:
                        observer.OnNext((T)step);
                        break;
                }
            }

            return Disposable.Empty;
        }),
        Push = _ => { },
        Complete = () => { },
    };

    private static ContractSource LiveCore<T>()
    {
        var subject = new Subject<T>();

        return new ContractSource
        {
            Observable = subject,
            Push = value => subject.OnNext((T)value),
            Complete = subject.OnCompleted,
        };
    }

    private static IDisposable WatchCore<T>(object observable, ContractTrace trace)
    {
        var terminated = false;

        return ((IObservable<T>)observable).Subscribe(
            _ =>
            {
                if (terminated)
                {
                    trace.AfterTerminal++;
                }
                else
                {
                    trace.Next++;
                }
            },
            error =>
            {
                trace.Error = error;
                terminated = true;
            },
            () =>
            {
                trace.Completed = true;
                terminated = true;
            });
    }
}
