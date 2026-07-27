using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using DynamicData.Binding;

using Microsoft.Reactive.Testing;

namespace DynamicData.Tests.RxContract;

/// <summary>
/// A stand-in for any interface the synthesizer cannot construct concretely.
/// </summary>
public class ContractStub : DispatchProxy
{
    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod is null || targetMethod.ReturnType == typeof(void) || !targetMethod.ReturnType.IsValueType
            ? null
            : Activator.CreateInstance(targetMethod.ReturnType);
}

/// <summary>
/// Binds open generic operator definitions to the stream being driven and manufactures the remaining arguments.
/// </summary>
internal static class ContractSynth
{
    private static readonly Type[] Candidates = [typeof(ContractItem), typeof(string), typeof(int), typeof(object)];

    private static readonly Type[] KeyCandidates = [typeof(string), typeof(int), typeof(ContractItem), typeof(object)];

    /// <summary>
    /// Unifies the declared first parameter with the stream to be driven, then searches only for the type
    /// arguments unification left open.
    /// </summary>
    /// <param name="definition">The operator definition.</param>
    /// <param name="wantedFirstParameter">The stream type the audit intends to drive.</param>
    /// <returns>The closed method, or <see langword="null"/> when the operator does not accept that stream.</returns>
    public static MethodInfo? Bind(MethodInfo definition, Type wantedFirstParameter)
    {
        if (!definition.IsGenericMethodDefinition)
        {
            return definition.GetParameters()[0].ParameterType == wantedFirstParameter ? definition : null;
        }

        var parameters = definition.GetGenericArguments();
        var bound = new Dictionary<Type, Type>();
        if (!Unify(definition.GetParameters()[0].ParameterType, wantedFirstParameter, bound))
        {
            return null;
        }

        var open = parameters.Where(tp => !bound.ContainsKey(tp)).ToArray();
        var options = open.Select(tp => tp.Name.Contains("Key", StringComparison.Ordinal) ? KeyCandidates : Candidates).ToArray();

        foreach (var combination in Cartesian(options))
        {
            var arguments = parameters.Select(tp => bound.TryGetValue(tp, out var known) ? known : combination[Array.IndexOf(open, tp)]).ToArray();

            MethodInfo closed;
            try
            {
                closed = definition.MakeGenericMethod(arguments);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (closed.GetParameters()[0].ParameterType == wantedFirstParameter)
            {
                return closed;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to manufacture a value for an operator parameter.
    /// </summary>
    /// <param name="type">The parameter type.</param>
    /// <param name="scheduler">The scheduler handed to any operator which accepts one.</param>
    /// <param name="value">The manufactured value.</param>
    /// <returns><see langword="true"/> when a value could be manufactured.</returns>
    public static bool TrySynth(Type type, TestScheduler scheduler, out object? value)
    {
        try
        {
            return Core(type, scheduler, out value);
        }
        catch (Exception)
        {
            value = null;
            return false;
        }
    }

    private static bool Unify(Type declared, Type actual, Dictionary<Type, Type> bound)
    {
        if (declared.IsGenericParameter)
        {
            if (bound.TryGetValue(declared, out var existing))
            {
                return existing == actual;
            }

            bound[declared] = actual;
            return true;
        }

        if (declared.IsArray)
        {
            return actual.IsArray && Unify(declared.GetElementType()!, actual.GetElementType()!, bound);
        }

        if (declared.IsGenericType)
        {
            if (!actual.IsGenericType || declared.GetGenericTypeDefinition() != actual.GetGenericTypeDefinition())
            {
                return false;
            }

            var declaredArguments = declared.GetGenericArguments();
            var actualArguments = actual.GetGenericArguments();

            return !declaredArguments.Where((t, i) => !Unify(t, actualArguments[i], bound)).Any();
        }

        return declared == actual;
    }

    private static IEnumerable<Type[]> Cartesian(Type[][] options)
    {
        if (options.Length == 0)
        {
            yield return [];
            yield break;
        }

        var indices = new int[options.Length];
        while (true)
        {
            yield return [.. options.Select((o, i) => o[indices[i]])];

            var position = options.Length - 1;
            while (position >= 0 && ++indices[position] == options[position].Length)
            {
                indices[position] = 0;
                position--;
            }

            if (position < 0)
            {
                yield break;
            }
        }
    }

    private static bool Core(Type type, TestScheduler scheduler, out object? value)
    {
        value = null;

        if (type.IsByRef)
        {
            return true;
        }

        if (type == typeof(Action))
        {
            value = (Action)(() => { });
            return true;
        }

        if (type == typeof(CancellationToken))
        {
            value = CancellationToken.None;
            return true;
        }

        if (type == typeof(string))
        {
            value = "k1";
            return true;
        }

        if (type == typeof(TimeSpan))
        {
            value = TimeSpan.FromMinutes(10);
            return true;
        }

        if (type == typeof(bool))
        {
            value = false;
            return true;
        }

        if (type == typeof(int))
        {
            value = 10;
            return true;
        }

        if (type == typeof(ContractItem) || type == typeof(object))
        {
            value = new ContractItem("k1", 1);
            return true;
        }

        if (type == typeof(Unit))
        {
            value = Unit.Default;
            return true;
        }

        if (type.IsEnum)
        {
            value = Enum.GetValues(type).GetValue(0);
            return true;
        }

        if (typeof(IScheduler).IsAssignableFrom(type))
        {
            value = scheduler;
            return true;
        }

        if (type.IsPrimitive)
        {
            value = Activator.CreateInstance(type);
            return true;
        }

        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return true;
        }

        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            if (!Core(element, scheduler, out var single))
            {
                return false;
            }

            var array = Array.CreateInstance(element, 1);
            array.SetValue(single, 0);
            value = array;
            return true;
        }

        if (type.IsGenericType && TryGeneric(type, scheduler, out value))
        {
            return true;
        }

        if (type.IsValueType)
        {
            value = Activator.CreateInstance(type);
            return true;
        }

        if (type.IsInterface)
        {
            var create = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);

            value = create.MakeGenericMethod(type, typeof(ContractStub)).Invoke(null, null);
            return true;
        }

        foreach (var constructor in type.GetConstructors().OrderBy(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            var ok = true;
            for (var i = 0; i < parameters.Length && ok; i++)
            {
                ok = TrySynth(parameters[i].ParameterType, scheduler, out arguments[i]);
            }

            if (!ok)
            {
                continue;
            }

            value = constructor.Invoke(arguments);
            return true;
        }

        return false;
    }

    private static bool TryGeneric(Type type, TestScheduler scheduler, out object? value)
    {
        value = null;
        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();

        if (definition == typeof(IObservable<>))
        {
            value = MakeObservable(arguments[0], scheduler);
            return true;
        }

        if (definition == typeof(IComparer<>))
        {
            value = typeof(Comparer<>).MakeGenericType(arguments[0]).GetProperty("Default")!.GetValue(null);
            return true;
        }

        if (definition == typeof(IEqualityComparer<>))
        {
            value = typeof(EqualityComparer<>).MakeGenericType(arguments[0]).GetProperty("Default")!.GetValue(null);
            return true;
        }

        if (definition == typeof(Task<>))
        {
            var inner = TrySynth(arguments[0], scheduler, out var result) ? result : null;
            value = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(arguments[0]).Invoke(null, [inner]);
            return true;
        }

        if (definition == typeof(Expression<>))
        {
            return TryExpression(arguments[0], out value);
        }

        if (definition.Name.StartsWith("Func`", StringComparison.Ordinal))
        {
            return TryFunc(type, scheduler, out value);
        }

        if (definition.Name.StartsWith("Action`", StringComparison.Ordinal))
        {
            var parameters = arguments.Select((a, i) => Expression.Parameter(a, "p" + i)).ToArray();
            value = Expression.Lambda(type, Expression.Empty(), parameters).Compile();
            return true;
        }

        if (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IReadOnlyList<>)
            || definition == typeof(ICollection<>) || definition == typeof(IList<>) || definition == typeof(List<>))
        {
            var listType = typeof(List<>).MakeGenericType(arguments[0]);
            var list = Activator.CreateInstance(listType)!;
            if (TrySynth(arguments[0], scheduler, out var single) && single is not null)
            {
                listType.GetMethod("Add")!.Invoke(list, [single]);
            }

            value = list;
            return true;
        }

        if (definition == typeof(IObservableCollection<>) || definition == typeof(ObservableCollectionExtended<>) || definition == typeof(ObservableCollection<>))
        {
            value = Activator.CreateInstance(typeof(ObservableCollectionExtended<>).MakeGenericType(arguments[0]));
            return true;
        }

        if (definition == typeof(BindingList<>))
        {
            value = Activator.CreateInstance(type);
            return true;
        }

        if (definition == typeof(ReadOnlyObservableCollection<>))
        {
            var backing = Activator.CreateInstance(typeof(ObservableCollection<>).MakeGenericType(arguments[0]))!;
            value = Activator.CreateInstance(type, backing);
            return true;
        }

        if (definition == typeof(ISourceList<>) || definition == typeof(IObservableList<>))
        {
            value = typeof(SourceList<>).MakeGenericType(arguments[0]).GetConstructors()[0].Invoke([null]);
            return true;
        }

        if (definition == typeof(ISourceCache<,>) || definition == typeof(IObservableCache<,>) || definition == typeof(IIntermediateCache<,>))
        {
            if (!TryFunc(typeof(Func<,>).MakeGenericType(arguments[0], arguments[1]), scheduler, out var selector) || selector is null)
            {
                return false;
            }

            value = Activator.CreateInstance(typeof(SourceCache<,>).MakeGenericType(arguments[0], arguments[1]), selector);
            return true;
        }

        return false;
    }

    private static object MakeObservable(Type element, TestScheduler scheduler)
    {
        // Secondary changeset streams complete straight away so that terminal propagation is never blocked by them.
        var isChangeSet = element.IsGenericType && element.GetGenericTypeDefinition().Name.StartsWith("IChangeSet", StringComparison.Ordinal);

        if (isChangeSet || !TrySynth(element, scheduler, out var single) || single is null)
        {
            return typeof(Observable).GetMethods().First(m => m.Name == "Empty" && m.GetParameters().Length == 0)
                .MakeGenericMethod(element).Invoke(null, null)!;
        }

        return typeof(Observable).GetMethods().First(m => m.Name == "Return" && m.GetParameters().Length == 1)
            .MakeGenericMethod(element).Invoke(null, [single])!;
    }

    private static bool TryExpression(Type funcType, out object? value)
    {
        value = null;
        var arguments = funcType.GetGenericArguments();
        if (arguments.Length != 2 || arguments[0] != typeof(ContractItem))
        {
            return false;
        }

        var parameter = Expression.Parameter(typeof(ContractItem), "x");
        if (arguments[1] == typeof(ContractItem))
        {
            value = Expression.Lambda(funcType, parameter, parameter);
            return true;
        }

        var member = arguments[1] == typeof(string) ? nameof(ContractItem.Name)
            : arguments[1] == typeof(int) ? nameof(ContractItem.Value)
            : null;

        if (member is null)
        {
            return false;
        }

        value = Expression.Lambda(funcType, Expression.Property(parameter, member), parameter);
        return true;
    }

    private static bool TryFunc(Type type, TestScheduler scheduler, out object? value)
    {
        value = null;
        var arguments = type.GetGenericArguments();
        var returns = arguments[^1];
        var parameters = arguments[..^1].Select((a, i) => Expression.Parameter(a, "p" + i)).ToArray();

        // Identity, key extraction and predicates all matter: a constant would collapse keys or filter everything out.
        if (parameters.Length >= 1 && parameters[0].Type == returns)
        {
            value = Expression.Lambda(type, parameters[0], parameters).Compile();
            return true;
        }

        if (returns == typeof(bool))
        {
            value = Expression.Lambda(type, Expression.Constant(true), parameters).Compile();
            return true;
        }

        if (parameters.Length >= 1 && parameters[0].Type == typeof(ContractItem) && (returns == typeof(string) || returns == typeof(int)))
        {
            var member = returns == typeof(string) ? nameof(ContractItem.Name) : nameof(ContractItem.Value);
            value = Expression.Lambda(type, Expression.Property(parameters[0], member), parameters).Compile();
            return true;
        }

        if (!TrySynth(returns, scheduler, out var result))
        {
            return false;
        }

        value = Expression.Lambda(type, Expression.Constant(result, returns), parameters).Compile();
        return true;
    }
}
