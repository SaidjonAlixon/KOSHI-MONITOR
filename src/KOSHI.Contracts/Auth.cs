using System.ComponentModel.DataAnnotations;

namespace KOSHI.Contracts;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(256, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;
}

public sealed record LoginResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    CurrentUserDto User,
    bool MfaRequired);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    Guid? DepartmentScopeId);
