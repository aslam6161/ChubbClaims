using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.ValueObjects;

/// <summary>Human-facing claim reference, e.g. <c>CLM-MY-2026-000123</c>. The sequence comes from the database.</summary>
public readonly record struct ClaimNumber
{
    public string Value { get; }

    private ClaimNumber(string value) => Value = value;

    public static ClaimNumber Create(Market market, int year, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return new ClaimNumber($"CLM-{market}-{year}-{sequence:D6}");
    }

    public override string ToString() => Value;
}
