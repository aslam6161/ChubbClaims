using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Service.DTOs.Reports;

/// <summary>
/// Outstanding liability. Built from Kafka events, so it is eventually consistent: each row has
/// <c>LastEventAt</c> and the UI should show how fresh it is. Never summed across currencies.
/// </summary>
public sealed record ExposureReportResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ExposureRowResponse> Rows,
    IReadOnlyList<CurrencyTotalResponse> TotalsByCurrency);

public sealed record ExposureRowResponse(
    Market Market, string Currency, ClaimType ClaimType, int OpenClaims, decimal OutstandingReserve, DateTimeOffset LastEventAt);

public sealed record CurrencyTotalResponse(string Currency, int OpenClaims, decimal OutstandingReserve, DateTimeOffset LastEventAt);
