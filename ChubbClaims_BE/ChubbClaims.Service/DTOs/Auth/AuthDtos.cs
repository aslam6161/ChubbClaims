using System.ComponentModel.DataAnnotations;
using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Service.DTOs.Auth;

public sealed record DevLoginRequest([Required, EmailAddress, MaxLength(256)] string Email);

public sealed record DevLoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);

public sealed record UserResponse(Guid Id, string Name, string Email, UserRole Role, Market? Market);
