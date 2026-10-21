using Firin.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace Firin.Api.Auth;

public sealed class PinHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public static bool IsValidFormat(string? pin) => pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    public string Hash(string pin) => _hasher.HashPassword(null!, pin);

    public bool Verify(string hash, string pin) =>
        _hasher.VerifyHashedPassword(null!, hash, pin) != PasswordVerificationResult.Failed;
}
