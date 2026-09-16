using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.Auth;

public record LoginRequest(string? Username, string? Password);
public record LoginResponse(string Token, string Username);

/// <summary>
/// Shared login endpoint — every service gets its own /api/auth/login
/// simply by referencing LoanPlatform.Common, same auto-discovery pattern
/// as SystemController. Because all four services are initialized with
/// the same JwtSecret and demo credentials at startup (see each
/// Program.cs's main()), a token obtained by logging into ANY one
/// service's /api/auth/login is valid at all four — you don't need to
/// log into each service separately.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserStore _users;
    private readonly IJwtService _jwt;
    private readonly ILogger<AuthController> _logger;

    private const string DummyHash = "AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public AuthController(IUserStore users, IJwtService jwt, ILogger<AuthController> logger)
    {
        _users = users;
        _jwt = jwt;
        _logger = logger;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            return BadRequest(new { error = "username and password are required." });

        var user = _users.FindByUsername(request.Username);
        var passwordOk = _users.VerifyPassword(request.Password, user?.PasswordHash ?? DummyHash);

        if (user is null || !passwordOk)
        {
            _logger.LogWarning("Login attempt failed for username {Username}", request.Username);
            return Unauthorized(new { error = "Invalid username or password." });
        }

        var token = _jwt.SignToken(user.Id, user.Username);
        _logger.LogInformation("Login succeeded for {Username}", user.Username);

        return Ok(new LoginResponse(token, user.Username));
    }
}
