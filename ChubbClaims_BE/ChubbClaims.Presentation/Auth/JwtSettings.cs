using System.ComponentModel.DataAnnotations;

namespace ChubbClaims.Presentation.Auth;

/// <summary>Bound from "Jwt". Dev-only symmetric key; a real deployment validates tokens from an identity provider.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = null!;
    [Required] public string Audience { get; set; } = null!;

    /// <summary>HMAC-SHA256 key: at least 32 bytes.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = null!;

    [Range(1, 24 * 60)] public int LifetimeMinutes { get; set; } = 480;
}
