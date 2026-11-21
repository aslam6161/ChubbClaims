using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.ValueObjects;

public sealed record PropertyDetails
{
    public string PropertyAddress { get; }
    public DamageType DamageType { get; }

    public PropertyDetails(string propertyAddress, DamageType damageType)
    {
        PropertyAddress = Guard.Required(propertyAddress, "Property address");
        DamageType = damageType;
    }
}
