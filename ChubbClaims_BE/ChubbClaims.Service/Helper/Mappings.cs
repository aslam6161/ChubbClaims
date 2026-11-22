using ChubbClaims.Domain.Constants;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Service.DTOs.Auth;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.DTOs.Policies;

namespace ChubbClaims.Service.Helper;

/// <summary>Hand-written mapping (no AutoMapper): explicit, debuggable, and the compiler checks it.</summary>
public static class Mappings
{
    public static UserResponse ToResponse(this User u) => new(u.Id, u.Name, u.Email, u.Role, u.Market);

    public static PolicyResponse ToResponse(this Policy p, DateTimeOffset now, int maxIncidentAgeDays)
    {
        var reportable = p.ReportableIncidentDates(Markets.LocalDate(p.Market, now), maxIncidentAgeDays);
        return new(p.PolicyNumber, p.ProductType, p.Market, p.Currency, p.EffectiveFrom, p.EffectiveTo, p.IsActive,
            reportable?.From, reportable?.To);
    }

    public static IncidentReport ToIncidentReport(this SubmitClaimRequest r) => new(
        r.Type,
        r.IncidentDate!.Value,
        r.IncidentLocation,
        r.Description,
        r.EstimatedLossAmount,
        r.Motor is null ? null : new MotorDetails(r.Motor.VehicleRegistration, r.Motor.ThirdPartyInvolved, r.Motor.PoliceReportNumber),
        r.Property is null ? null : new PropertyDetails(r.Property.PropertyAddress, r.Property.DamageType));

    public static ClaimSummaryResponse ToSummary(this Claim c) => new(
        c.Id, c.ClaimNumber, c.Type, c.Status, c.Market, c.Currency, c.PolicyNumber,
        c.IncidentDate, c.ReportedAt, c.SettlementAmount, c.DecisionAt);

    public static ClaimDetailResponse ToDetail(this Claim c, bool includeStaffFields) => new(
        c.Id, c.ClaimNumber, c.Type, c.Status, c.Market, c.Currency, c.PolicyNumber,
        c.IncidentDate, c.ReportedAt, c.IncidentLocation, c.Description, c.EstimatedLossAmount,
        c.MotorDetails is { } m ? new MotorDetailsDto(m.VehicleRegistration, m.ThirdPartyInvolved, m.PoliceReportNumber) : null,
        c.PropertyDetails is { } p ? new PropertyDetailsDto(p.PropertyAddress, p.DamageType) : null,
        c.SettlementAmount, c.PaidAmount, c.DecisionAt, c.ClosedAt, c.SlaDueAt,
        c.OpenInformationRequest?.ToResponse(),
        c.InformationRequests.OrderBy(r => r.RequestedAt).Select(ToResponse).ToList(),
        c.StatusHistory.OrderBy(h => h.At).Select(h => new StatusChangeResponse(h.From, h.To, h.ActorRole, h.At, h.Reason)).ToList(),
        includeStaffFields ? c.AssignedOfficerId : null,
        includeStaffFields ? c.Priority : null,
        includeStaffFields ? c.ReserveAmount : null,
        includeStaffFields ? c.OutstandingReserve : null,
        includeStaffFields
            ? c.ReserveHistory.OrderBy(h => h.At).Select(h => new ReserveChangeResponse(h.OldAmount, h.NewAmount, h.ActorId, h.At, h.Reason)).ToList()
            : null,
        includeStaffFields
            ? c.Notes.OrderBy(n => n.CreatedAt).Select(n => new NoteResponse(n.Id, n.AuthorId, n.Text, n.CreatedAt)).ToList()
            : null);

    private static InformationRequestResponse ToResponse(this InformationRequest r) =>
        new(r.Id, r.Question, r.RequestedAt, r.Response, r.RespondedAt, r.IsOpen);
}
