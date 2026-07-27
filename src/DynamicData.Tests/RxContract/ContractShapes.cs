using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;

namespace DynamicData.Tests.RxContract;

/// <summary>
/// Builds an instance of every changeset flavour the library consumes.
/// </summary>
/// <remarks>
/// These are constructed directly rather than by routing a source through the operator that would normally
/// produce them. Routing through a producing operator would contaminate the result whenever that operator is
/// itself defective, which is precisely what the audit is looking for.
/// </remarks>
internal static class ContractShapes
{
    private static readonly Assembly Library = typeof(ObservableCacheEx).Assembly;

    private static readonly Type Item = typeof(ContractItem);

    private static readonly Type Key = typeof(string);

    /// <summary>
    /// Resolves a type by simple name, including types which are not publicly visible.
    /// </summary>
    /// <param name="name">The simple type name.</param>
    /// <returns>The resolved type.</returns>
    public static Type Resolve(string name) => Library.GetTypes().First(x => x.Name == name);

    public static object CacheChangeSet() => new ChangeSet<ContractItem, string>([new Change<ContractItem, string>(ChangeReason.Add, "k1", new ContractItem("k1", 1))]);

    public static object ListChangeSet() => new ChangeSet<ContractItem>([new Change<ContractItem>(ListChangeReason.Add, new ContractItem("k1", 1), 0)]);

    public static object Cache()
    {
        var cache = new SourceCache<ContractItem, string>(x => x.Name);
        cache.AddOrUpdate(new ContractItem("k1", 1));
        return cache;
    }

    public static object List()
    {
        var list = new SourceList<ContractItem>();
        list.Add(new ContractItem("k1", 1));
        return list;
    }

    public static object Sorted() => New("SortedChangeSet`2", [Item, Key], KeyValueCollection(), CacheChanges());

    public static object Virtual() => New("VirtualChangeSet`2", [Item, Key], CacheChanges(), KeyValueCollection(), New("VirtualResponse", [], 1, 0, 1));

    public static object Paged() => New("PagedChangeSet`2", [Item, Key], KeyValueCollection(), CacheChanges(), New("PageResponse", [], 1, 1, 1, 1));

    public static object Distinct() => New("DistinctChangeSet`1", [typeof(int)], new List<Change<int, int>> { new(ChangeReason.Add, 1, 1) });

    public static object Grouped() => New("GroupChangeSet`3", [Item, Key, Key], EmptyChanges(typeof(IGroup<,,>).MakeGenericType(Item, Key, Key), Key));

    public static object ImmutableGrouped() => New("ImmutableGroupChangeSet`3", [Item, Key, Key], EmptyChanges(typeof(IGrouping<,,>).MakeGenericType(Item, Key, Key), Key));

    public static object AggregateFromCache() => New("AggregateEnumerator`2", [Item, Key], CacheChangeSet());

    public static object AggregateFromList() => New("AggregateEnumerator`1", [Item], ListChangeSet());

    public static object ContextChangeSet() => new ChangeSet<ContractItem, string, string>((List<Change<ContractItem, string>>)CacheChanges(), "ctx");

    public static object PagedContextChangeSet()
    {
        var context = New("PageContext`1", [Item], New("PageResponse", [], 1, 1, 1, 1), Comparer<ContractItem>.Default, new SortAndPageOptions());
        return Activator.CreateInstance(typeof(ChangeSet<,,>).MakeGenericType(Item, Key, context.GetType()), CacheChanges(), context)!;
    }

    public static object VirtualContextChangeSet()
    {
        var context = New("VirtualContext`1", [Item], New("VirtualResponse", [], 1, 0, 1), Comparer<ContractItem>.Default, new SortAndVirtualizeOptions());
        return Activator.CreateInstance(typeof(ChangeSet<,,>).MakeGenericType(Item, Key, context.GetType()), CacheChanges(), context)!;
    }

    public static object ObjectListChangeSet() => new ChangeSet<object>([new Change<object>(ListChangeReason.Add, new ContractItem("k1", 1), 0)]);

    public static object CacheStreamListChangeSet() =>
        new ChangeSet<IObservable<IChangeSet<ContractItem, string>>>(
            [new Change<IObservable<IChangeSet<ContractItem, string>>>(ListChangeReason.Add, Observable.Empty<IChangeSet<ContractItem, string>>(), 0)]);

    public static object ListStreamListChangeSet() =>
        new ChangeSet<IObservable<IChangeSet<ContractItem>>>(
            [new Change<IObservable<IChangeSet<ContractItem>>>(ListChangeReason.Add, Observable.Empty<IChangeSet<ContractItem>>(), 0)]);

    public static object BufferedCacheChangeSets() => new List<IChangeSet<ContractItem, string>> { (IChangeSet<ContractItem, string>)CacheChangeSet() };

    public static object BufferedListChangeSets() => new List<IChangeSet<ContractItem>> { (IChangeSet<ContractItem>)ListChangeSet() };

    private static object New(string name, Type[] generics, params object?[] args)
    {
        var type = Resolve(name);
        if (generics.Length > 0)
        {
            type = type.MakeGenericType(generics);
        }

        var constructor = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == args.Length);

        return constructor.Invoke(args);
    }

    private static object CacheChanges() => new List<Change<ContractItem, string>> { new(ChangeReason.Add, "k1", new ContractItem("k1", 1)) };

    private static object KeyValueCollection()
    {
        var pairs = new List<KeyValuePair<string, ContractItem>> { new("k1", new ContractItem("k1", 1)) };
        var comparer = Comparer<KeyValuePair<string, ContractItem>>.Create((a, b) => a.Value.CompareTo(b.Value));
        var sortReason = Resolve("SortReason");

        return New("KeyValueCollection`2", [Item, Key], pairs, comparer, Enum.GetValues(sortReason).GetValue(0), SortOptimisations.None);
    }

    private static object EmptyChanges(Type item, Type key) =>
        Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(Change<,>).MakeGenericType(item, key)))!;
}


