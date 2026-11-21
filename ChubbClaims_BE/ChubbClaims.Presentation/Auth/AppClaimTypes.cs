namespace ChubbClaims.Presentation.Auth;

/// <summary>JWT claim names. Short standard names; inbound claim mapping is off so they arrive unchanged.</summary>
public static class AppClaimTypes
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
    public const string Market = "market";
}
