using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

using DynamicData.Kernel;

using Microsoft.Reactive.Testing;

namespace DynamicData.Tests.RxContract;

/// <summary>
/// A stream flavour the audit knows how to drive.
/// </summary>
/// <param name="Domain">The collection type the stream belongs to.</param>
/// <param name="Stream">The observable type an operator must accept to be bound to this shape.</param>
/// <param name="Element">The element carried by the stream.</param>
/// <param name="Data">Produces one element.</param>
internal sealed record ContractShape(string Domain, Type Stream, Type Element, Func<object> Data);

/// <summary>
/// A single operator overload bound to a single stream flavour.
/// </summary>
/// <param name="Shape">The stream flavour driven into the operator.</param>
/// <param name="DeclaringType">The class declaring the extension method.</param>
/// <param name="Operator">The operator name.</param>
/// <param name="Signature">The parameters other than the source.</param>
/// <param name="Method">The closed generic method.</param>
/// <param name="ResultElement">The element carried by the operator's result.</param>
internal sealed record ContractProbe(ContractShape Shape, string DeclaringType, string Operator, string Signature, MethodInfo Method, Type ResultElement)
{
    /// <summary>
    /// Gets a stable identifier used for sorting and reporting.
    /// </summary>
    public string Id => $"{DeclaringType}.{Operator}({Signature})";
}

/// <summary>
/// Drives every operator overload with terminal events and records what the operator actually emits.
/// </summary>
/// <remarks>
/// This exists because <c>Switch</c> was found to silently drop <c>OnCompleted</c> and mishandle <c>OnError</c>.
/// The sweep answers the obvious follow up question for the whole library rather than one operator at a time.
/// Overloads are driven individually because they frequently do not share an implementation. <c>Sort(comparer)</c>
/// completes correctly while <c>Sort(observableComparer)</c> does not.
/// </remarks>
internal static class ContractSweep
{
    private const string Completes = "COMPLETE";

    /// <summary>
    /// Overloads which crash the process instead of returning.
    /// </summary>
    /// <remarks>
    /// A stack overflow cannot be caught, so these have to be skipped or they take the whole test host down.
    /// Each entry is a bug in its own right. Remove the entry when the underlying defect is fixed, and the
    /// overload rejoins the sweep automatically.
    /// </remarks>
    private static readonly string[] Fatal =
    [
        // Infinite recursion. The lone IComparer<T> argument binds the call back to the method making it.
        "ObservableListEx.MergeChangeSets(IComparer<ContractItem>)",
    ];

    private static readonly Exception Failure = new InvalidOperationException("audit");

    /// <summary>
    /// Runs the audit and renders a stable report for the requested domains.
    /// </summary>
    /// <param name="domains">The domains to include.</param>
    /// <returns>A deterministic, line oriented report.</returns>
    public static string Report(params string[] domains)
    {
        var probes = Discover()
            .Where(p => domains.Contains(p.Shape.Domain, StringComparer.Ordinal))
            .GroupBy(p => (p.Shape.Domain, p.Id))
            .Select(g => g.First())
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Shape.Domain, StringComparer.Ordinal)
            .ToList();

        var report = new StringBuilder();
        report.AppendLine("Each overload is subscribed, then given a terminal event, and what it emits is recorded.");
        report.AppendLine("'ok' means the operator honoured the observable contract.");
        report.AppendLine();

        foreach (var probe in probes)
        {
            var verdicts = Execute(probe);
            report.AppendLine(CultureFree($"{probe.Shape.Domain,-18} {Readable(probe.Id)}"));
            report.AppendLine(CultureFree($"    complete={verdicts.Complete}  error={verdicts.Error}  data-then-complete={verdicts.DataComplete}  data-then-error={verdicts.DataError}  dispose={verdicts.Dispose}"));
        }

        return report.ToString();
    }

    private static string CultureFree(FormattableString text) => FormattableString.Invariant(text);

    private static string Readable(string id) => id
        .Replace("ContractItem", "TObject", StringComparison.Ordinal)
        .Replace("String", "TKey", StringComparison.Ordinal)
        .Replace("Nullable<TimeSpan>", "TimeSpan?", StringComparison.Ordinal)
        .Replace("Boolean", "bool", StringComparison.Ordinal)
        .Replace("Int32", "int", StringComparison.Ordinal)
        .Replace("Int64", "long", StringComparison.Ordinal);

    private static ContractShape[] Shapes()
    {
        var item = typeof(ContractItem);
        var key = typeof(string);
        var cacheChangeSet = typeof(IChangeSet<ContractItem, string>);
        var listChangeSet = typeof(IChangeSet<ContractItem>);

        Type Stream(Type element) => typeof(IObservable<>).MakeGenericType(element);
        Type Internal(string name, params Type[] arguments) => ContractShapes.Resolve(name).MakeGenericType(arguments);

        return
        [
            new("cache", Stream(cacheChangeSet), cacheChangeSet, ContractShapes.CacheChangeSet),
            new("list", Stream(listChangeSet), listChangeSet, ContractShapes.ListChangeSet),
            new("cache", Stream(Stream(cacheChangeSet)), Stream(cacheChangeSet), () => Observable.Return((IChangeSet<ContractItem, string>)ContractShapes.CacheChangeSet())),
            new("list", Stream(Stream(listChangeSet)), Stream(listChangeSet), () => Observable.Return((IChangeSet<ContractItem>)ContractShapes.ListChangeSet())),
            new("cache", typeof(IObservable<IObservableCache<ContractItem, string>>), typeof(IObservableCache<ContractItem, string>), ContractShapes.Cache),
            new("list", typeof(IObservable<IObservableList<ContractItem>>), typeof(IObservableList<ContractItem>), ContractShapes.List),
            new("cache", Stream(Internal("ISortedChangeSet`2", item, key)), Internal("ISortedChangeSet`2", item, key), ContractShapes.Sorted),
            new("cache", Stream(Internal("IVirtualChangeSet`2", item, key)), Internal("IVirtualChangeSet`2", item, key), ContractShapes.Virtual),
            new("cache", Stream(Internal("IPagedChangeSet`2", item, key)), Internal("IPagedChangeSet`2", item, key), ContractShapes.Paged),
            new("cache", Stream(Internal("IDistinctChangeSet`1", typeof(int))), Internal("IDistinctChangeSet`1", typeof(int)), ContractShapes.Distinct),
            new("cache", Stream(Internal("IGroupChangeSet`3", item, key, key)), Internal("IGroupChangeSet`3", item, key, key), ContractShapes.Grouped),
            new("cache", Stream(Internal("IImmutableGroupChangeSet`3", item, key, key)), Internal("IImmutableGroupChangeSet`3", item, key, key), ContractShapes.ImmutableGrouped),
            new("cache", Stream(typeof(IChangeSet<ContractItem, string, string>)), typeof(IChangeSet<ContractItem, string, string>), ContractShapes.ContextChangeSet),
            new("cache", Stream(ContextOf(ContractShapes.PagedContextChangeSet())), ContextOf(ContractShapes.PagedContextChangeSet()), ContractShapes.PagedContextChangeSet),
            new("cache", Stream(ContextOf(ContractShapes.VirtualContextChangeSet())), ContextOf(ContractShapes.VirtualContextChangeSet()), ContractShapes.VirtualContextChangeSet),
            new("cache", Stream(typeof(IList<IChangeSet<ContractItem, string>>)), typeof(IList<IChangeSet<ContractItem, string>>), ContractShapes.BufferedCacheChangeSets),
            new("list", Stream(typeof(IList<IChangeSet<ContractItem>>)), typeof(IList<IChangeSet<ContractItem>>), ContractShapes.BufferedListChangeSets),
            new("list", typeof(IObservable<IChangeSet<object>>), typeof(IChangeSet<object>), ContractShapes.ObjectListChangeSet),
            new("list", Stream(typeof(IChangeSet<IObservable<IChangeSet<ContractItem, string>>>)), typeof(IChangeSet<IObservable<IChangeSet<ContractItem, string>>>), ContractShapes.CacheStreamListChangeSet),
            new("list", Stream(typeof(IChangeSet<IObservable<IChangeSet<ContractItem>>>)), typeof(IChangeSet<IObservable<IChangeSet<ContractItem>>>), ContractShapes.ListStreamListChangeSet),
            new("aggregation/cache", Stream(Internal("IAggregateChangeSet`1", item)), Internal("IAggregateChangeSet`1", item), ContractShapes.AggregateFromCache),
            new("aggregation/list", Stream(Internal("IAggregateChangeSet`1", item)), Internal("IAggregateChangeSet`1", item), ContractShapes.AggregateFromList),
            new("any", typeof(IObservable<ContractItem>), item, () => new ContractItem("k1", 1)),
            new("any", typeof(IObservable<Optional<ContractItem>>), typeof(Optional<ContractItem>), () => Optional.Some(new ContractItem("k1", 1))),
            new("any", typeof(IObservable<IEnumerable<ContractItem>>), typeof(IEnumerable<ContractItem>), () => new List<ContractItem> { new("k1", 1) }),
            new("any", typeof(IObservable<IReadOnlyCollection<ContractItem>>), typeof(IReadOnlyCollection<ContractItem>), () => new List<ContractItem> { new("k1", 1) }),
        ];
    }

    private static Type ContextOf(object changeSet) => changeSet.GetType().GetInterfaces().First(x => x.Name == "IChangeSet`3");

    private static IEnumerable<ContractProbe> Discover()
    {
        var shapes = Shapes();

        foreach (var declaring in typeof(ObservableCacheEx).Assembly.GetExportedTypes().Where(x => x.IsAbstract && x.IsSealed))
        {
            foreach (var method in declaring.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!method.IsDefined(typeof(ExtensionAttribute), false))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 0 || !parameters[0].ParameterType.IsGenericType || parameters[0].ParameterType.Name != "IObservable`1")
                {
                    continue;
                }

                foreach (var shape in shapes)
                {
                    var closed = ContractSynth.Bind(method, shape.Stream);
                    if (closed is null)
                    {
                        continue;
                    }

                    // Operators which do not return an observable have no terminal behaviour to assert.
                    var returns = closed.ReturnType;
                    if (!returns.IsGenericType || returns.GetGenericTypeDefinition() != typeof(IObservable<>))
                    {
                        continue;
                    }

                    if (!TryArguments(closed, out _, out _))
                    {
                        continue;
                    }

                    var signature = string.Join(", ", closed.GetParameters().Skip(1).Select(x => Pretty(x.ParameterType)));
                    var probe = new ContractProbe(shape, declaring.Name, method.Name, signature, closed, returns.GetGenericArguments()[0]);

                    if (Fatal.Contains(probe.Id, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    yield return probe;
                }
            }
        }
    }

    private static bool TryArguments(MethodInfo method, out object?[] arguments, out TestScheduler scheduler)
    {
        scheduler = new TestScheduler();
        var parameters = method.GetParameters();
        arguments = new object?[parameters.Length];

        for (var i = 1; i < parameters.Length; i++)
        {
            // Optional parameters carry meaning. MergeChangeSets takes 'bool completable = true', and inventing
            // a value for it asks the operator not to complete and then reports that as a defect.
            if (parameters[i].HasDefaultValue)
            {
                arguments[i] = parameters[i].DefaultValue;
                continue;
            }

            if (!ContractSynth.TrySynth(parameters[i].ParameterType, scheduler, out arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string Pretty(Type type) => type.IsByRef
        ? "out " + Pretty(type.GetElementType()!)
        : type.IsArray
            ? Pretty(type.GetElementType()!) + "[]"
            : type.IsGenericType
                ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] + "<" + string.Join(",", type.GetGenericArguments().Select(Pretty)) + ">"
                : type.Name;

    private static (string Complete, string Error, string DataComplete, string DataError, string Dispose) Execute(ContractProbe probe)
    {
        var verdicts = new string[5];
        Array.Fill(verdicts, "timeout");

        var finished = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            try
            {
                verdicts[0] = Verdict(Drive(probe, [Completes]), expectError: false);
                verdicts[1] = Verdict(Drive(probe, [Failure]), expectError: true);
                verdicts[2] = Verdict(Drive(probe, [null, Completes]), expectError: false);
                verdicts[3] = Verdict(Drive(probe, [null, Failure]), expectError: true);
                verdicts[4] = Disposal(probe);
            }
            finally
            {
                finished.Set();
            }
        })
        {
            IsBackground = true,
        };

        worker.Start();
        finished.Wait(TimeSpan.FromSeconds(30));

        return (verdicts[0], verdicts[1], verdicts[2], verdicts[3], verdicts[4]);
    }

    private static string Verdict(ContractTrace trace, bool expectError)
    {
        if (trace.Threw is not null)
        {
            return "THROWS-OUT-OF-SUBSCRIBE";
        }

        if (trace.AfterTerminal > 0)
        {
            return "EMITS-AFTER-TERMINAL";
        }

        if (expectError)
        {
            return trace.Error is not null ? "ok" : trace.Completed ? "ERROR-BECOMES-COMPLETE" : "ERROR-LOST";
        }

        return trace.Completed ? "ok" : trace.Error is not null ? "COMPLETE-BECOMES-ERROR" : "NO-COMPLETE";
    }

    private static ContractTrace Drive(ContractProbe probe, object?[] script)
    {
        var steps = script.Select(s => s ?? probe.Shape.Data()).ToArray();
        var source = ContractSource.Scripted(probe.Shape.Element, steps);

        return Subscribe(probe, source, null);
    }

    private static string Disposal(ContractProbe probe)
    {
        var source = ContractSource.Live(probe.Shape.Element);

        var trace = Subscribe(probe, source, subscription =>
        {
            source.Push(probe.Shape.Data());
            subscription.Dispose();
            source.Push(probe.Shape.Data());
            source.Complete();
        });

        if (trace.Threw is not null)
        {
            return "THROWS-OUT-OF-SUBSCRIBE";
        }

        return trace.Completed ? "NOTIFIES-AFTER-DISPOSE" : "ok";
    }

    private static ContractTrace Subscribe(ContractProbe probe, ContractSource source, Action<IDisposable>? then)
    {
        var trace = new ContractTrace();

        if (!TryArguments(probe.Method, out var arguments, out var scheduler))
        {
            trace.Threw = new InvalidOperationException("arguments");
            return trace;
        }

        arguments[0] = source.Observable;

        try
        {
            var result = probe.Method.Invoke(null, arguments);
            if (result is null)
            {
                trace.Threw = new InvalidOperationException("null result");
                return trace;
            }

            var subscription = ContractSource.Watch(probe.ResultElement, result, trace);

            if (then is null)
            {
                subscription.Dispose();
            }
            else
            {
                then(subscription);
            }

            // Virtual time means nothing blocks; draining it lets time based operators reach their terminal state.
            scheduler.AdvanceBy(TimeSpan.FromDays(1).Ticks);
        }
        catch (TargetInvocationException ex)
        {
            trace.Threw = ex.InnerException ?? ex;
        }
        catch (Exception ex)
        {
            trace.Threw = ex;
        }

        return trace;
    }
}
