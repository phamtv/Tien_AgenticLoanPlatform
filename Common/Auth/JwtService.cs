using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LoanPlatform.Common.Auth;

public record JwtPayload(string Sub, string Username, long Iat, long Exp);

public interface IJwtService
{
    void Initialize(string secret);
    string SignToken(string userId, string username);
    JwtPayload? VerifyToken(string token);
}

/// <summary>
/// Hand-rolled HS256 JWT — same implementation and same reasoning as
/// backend-dotnet's JwtService.cs earlier in this broader project:
/// Microsoft.AspNetCore.Authentication.JwtBearer and
/// System.IdentityModel.Tokens.Jwt are both NuGet packages, unavailable
/// in this sandbox. This uses only System.Security.Cryptography — real
/// HMAC-SHA256 signing and constant-time verification, not a shortcut.
///
/// Placed in Common (not duplicated per service) so all four services
/// share one signing secret loaded from the same config key
/// (JwtSecret, via IKeyVaultService) — meaning a token issued by any one
/// service's /api/auth/login is valid at all four. That's deliberate: this
/// models a shared-identity-provider pattern (the way a real deployment
/// might have one auth-service or Azure AD issuing tokens every other
/// service trusts), without needing an actual separate 5th service for
/// what is, in this demo, one shared demo account.
/// </summary>
public class JwtService : IJwtService
{
    private byte[]? _secretBytes;
    private readonly TimeSpan _expiresIn = TimeSpan.FromHours(8);

    public void Initialize(string secret)
    {
        _secretBytes = Encoding.UTF8.GetBytes(secret);
    }

    public string SignToken(string userId, string username)
    {
        if (_secretBytes is null)
            throw new InvalidOperationException("JwtService not initialized. Call Initialize(secret) at startup.");

        var now = DateTimeOffset.UtcNow;
        var header = new { alg = "HS256", typ = "JWT" };
        var payload = new { sub = userId, username, iat = now.ToUnixTimeSeconds(), exp = now.Add(_expiresIn).ToUnixTimeSeconds() };

        var headerSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = $"{headerSegment}.{payloadSegment}";

        using var hmac = new HMACSHA256(_secretBytes);
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    public JwtPayload? VerifyToken(string token)
    {
        if (_secretBytes is null)
            throw new InvalidOperationException("JwtService not initialized. Call Initialize(secret) at startup.");

        var parts = token.Split('.');
        if (parts.Length != 3) return null;

        var signingInput = $"{parts[0]}.{parts[1]}";
        using var hmac = new HMACSHA256(_secretBytes);
        var expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        var actualSignature = Base64UrlDecode(parts[2]);

        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature)) return null;

        var payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[1]));
        var exp = payload.GetProperty("exp").GetInt64();
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return null; // expired

        return new JwtPayload(
            Sub: payload.GetProperty("sub").GetString()!,
            Username: payload.GetProperty("username").GetString()!,
            Iat: payload.GetProperty("iat").GetInt64(),
            Exp: exp
        );
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
