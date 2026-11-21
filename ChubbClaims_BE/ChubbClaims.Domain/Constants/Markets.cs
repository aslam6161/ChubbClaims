using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.Constants;

/// <summary>Static facts about each market: its currency and its local time zone.</summary>
public static class Markets
{
    private static readonly IReadOnlyDictionary<Market, (string Currency, string TimeZoneId)> Info =
        new Dictionary<Market, (string, string)>
        {
            [Market.MY] = ("MYR", "Asia/Kuala_Lumpur"),
            [Market.SG] = ("SGD", "Asia/Singapore"),
            [Market.HK] = ("HKD", "Asia/Hong_Kong"),
            [Market.TH] = ("THB", "Asia/Bangkok"),
            [Market.ID] = ("IDR", "Asia/Jakarta"),
            [Market.AU] = ("AUD", "Australia/Sydney"), // one zone for the demo; AU spans several
        };

    public static string CurrencyOf(Market market) => Info[market].Currency;

    /// <summary>
    /// The calendar date in the market right now. Incident dates are local dates, and every APAC
    /// market is ahead of UTC: at 08:00 in Sydney it is still "yesterday" in UTC, so comparing an
    /// incident date to the UTC date would wrongly reject a same-day claim as "in the future".
    /// </summary>
    public static DateOnly LocalDate(Market market, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Info[market].TimeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
    }
}
