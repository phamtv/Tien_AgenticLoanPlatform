using System.Security.Cryptography;

namespace LoanPlatform.Common.Auth;

public record User(string Id, string Username, string PasswordHash);

public interface IUserStore
{
    void Initialize(string username, string password);
    User? FindByUsername(string username);
    bool VerifyPassword(string plaintext, string storedHash);
}

/// <summary>
/// Same PBKDF2-based demo user store as backend-dotnet's UserStore.cs —
/// one shared account across all four services (see JwtService.cs's
/// remarks on why sharing one identity is deliberate here). PBKDF2
/// instead of BCrypt.Net for the same reason as everywhere else: no NuGet
/// access in this sandbox, and PBKDF2 is a legitimate BCL-native choice,
/// not a downgrade.
/// </summary>
public class InMemoryUserStore : IUserStore
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    private User? _user;

    public void Initialize(string username, string password)
    {
        _user = new User("u-1", username, HashPassword(password));
    }

    public User? FindByUsername(string username) =>
        _user is not null && _user.Username == username ? _user : null;

    public bool VerifyPassword(string plaintext, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(plaintext, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
}
