using System;
using System.ComponentModel;

namespace DynamicData.Tests.RxContract;

/// <summary>
/// The item type used to drive every operator under audit.
/// </summary>
/// <remarks>
/// It deliberately satisfies the widest set of generic constraints used across the library so that the
/// largest possible number of overloads can be bound and exercised.
/// </remarks>
public sealed class ContractItem : INotifyPropertyChanged, IDisposable, IComparable<ContractItem>, IEquatable<ContractItem>
{
    private int _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractItem"/> class.
    /// </summary>
    public ContractItem()
        : this("k0", 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractItem"/> class.
    /// </summary>
    /// <param name="name">The name, which doubles as the key.</param>
    /// <param name="value">An arbitrary numeric value.</param>
    public ContractItem(string name, int value)
    {
        Name = name;
        _value = value;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the name, which doubles as the key.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value which raises change notifications.
    /// </summary>
    public int Value
    {
        get => _value;
        set
        {
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    /// <inheritdoc/>
    public int CompareTo(ContractItem? other) => string.CompareOrdinal(Name, other?.Name);

    /// <inheritdoc/>
    public bool Equals(ContractItem? other) => other is not null && other.Name == Name;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContractItem);

    /// <inheritdoc/>
    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
