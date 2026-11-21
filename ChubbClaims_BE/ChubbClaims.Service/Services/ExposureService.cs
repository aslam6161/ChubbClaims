using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Reports;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Service.Services;

/// <summary>Reads the exposure projection. Portfolio-wide (all markets): managers see the whole region.</summary>
public sealed class ExposureService(IExposureRepository exposure, TimeProvider clock) : IExposureService
{
    public async Task<ExposureReportResponse> GetAsync(CancellationToken ct)
    {
        var rows = await exposure.GetAllAsync(ct);

        return new ExposureReportResponse(
            clock.GetUtcNow(),
            rows.Select(r => new ExposureRowResponse(r.Market, r.Currency, r.ClaimType, r.OpenClaims, r.OutstandingReserve, r.LastEventAt)).ToList(),
            // Totals per currency only: adding MYR to IDR would be meaningless without FX (out of scope).
            rows.GroupBy(r => r.Currency)
                .Select(g => new CurrencyTotalResponse(g.Key, g.Sum(r => r.OpenClaims), g.Sum(r => r.OutstandingReserve), g.Max(r => r.LastEventAt)))
                .OrderBy(t => t.Currency)
                .ToList());
    }
}
