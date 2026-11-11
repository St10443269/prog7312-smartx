namespace SmartX.Api.Topology;

/// <summary>
/// The cost of using one wireless link, as an estimate of the energy needed to
/// send a message over it (simulated, see SimulatedLayout). Operators are
/// overloaded the same way as MeterReading in Part 1, so costs can be added
/// up and compared directly: path = a + b, and the priority queue in Prim's
/// algorithm orders links with the less-than operator.
/// </summary>
public readonly struct LinkCost : IComparable<LinkCost>, IEquatable<LinkCost>
{
    public double Value { get; }

    public LinkCost(double value)
    {
        Value = value;
    }

    public static LinkCost Zero => new(0);

    public static LinkCost operator +(LinkCost a, LinkCost b) => new(a.Value + b.Value);

    public static bool operator <(LinkCost a, LinkCost b) => a.Value < b.Value;
    public static bool operator >(LinkCost a, LinkCost b) => a.Value > b.Value;
    public static bool operator <=(LinkCost a, LinkCost b) => a.Value <= b.Value;
    public static bool operator >=(LinkCost a, LinkCost b) => a.Value >= b.Value;
    public static bool operator ==(LinkCost a, LinkCost b) => a.Value.Equals(b.Value);
    public static bool operator !=(LinkCost a, LinkCost b) => !a.Value.Equals(b.Value);

    public int CompareTo(LinkCost other) => Value.CompareTo(other.Value);

    public bool Equals(LinkCost other) => Value.Equals(other.Value);

    public override bool Equals(object? obj) => obj is LinkCost other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
