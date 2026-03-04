namespace Juxtens.DeviceManager;

public enum DeviceStateKind
{
    Enabled,
    Disabled,
    NotFound,
    Unknown,
    Problem
}

public readonly struct DeviceState
{
    public DeviceStateKind Kind { get; }
    public uint? ProblemCode { get; }

    private DeviceState(DeviceStateKind kind, uint? problemCode = null)
    {
        Kind = kind;
        ProblemCode = problemCode;
    }

    public static DeviceState Enabled => new(DeviceStateKind.Enabled);
    public static DeviceState Disabled => new(DeviceStateKind.Disabled);
    public static DeviceState NotFound => new(DeviceStateKind.NotFound);
    public static DeviceState Unknown => new(DeviceStateKind.Unknown);
    public static DeviceState Problem(uint problemCode) => new(DeviceStateKind.Problem, problemCode);

    public override string ToString() =>
        Kind == DeviceStateKind.Problem && ProblemCode.HasValue
            ? $"Problem (Code: 0x{ProblemCode.Value:X})"
            : Kind.ToString();

    public bool Equals(DeviceState other) =>
        Kind == other.Kind && ProblemCode == other.ProblemCode;

    public override bool Equals(object? obj) =>
        obj is DeviceState other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, ProblemCode);

    public static bool operator ==(DeviceState left, DeviceState right) => left.Equals(right);
    public static bool operator !=(DeviceState left, DeviceState right) => !left.Equals(right);
}
