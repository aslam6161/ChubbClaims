namespace ChubbClaims.Domain.ValueObjects;

public sealed record MotorDetails
{
    public string VehicleRegistration { get; }
    public bool ThirdPartyInvolved { get; }
    public string? PoliceReportNumber { get; }

    public MotorDetails(string vehicleRegistration, bool thirdPartyInvolved, string? policeReportNumber)
    {
        VehicleRegistration = Guard.Required(vehicleRegistration, "Vehicle registration");
        ThirdPartyInvolved = thirdPartyInvolved;
        PoliceReportNumber = string.IsNullOrWhiteSpace(policeReportNumber) ? null : policeReportNumber.Trim();
    }
}
