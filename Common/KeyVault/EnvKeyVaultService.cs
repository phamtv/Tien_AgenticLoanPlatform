using Microsoft.Extensions.Configuration;

namespace LoanPlatform.Common.KeyVault;

public class EnvKeyVaultService : IKeyVaultService
{
    private readonly IConfiguration _config;

    public EnvKeyVaultService(IConfiguration config)
    {
        _config = config;
    }

    public Task<string> GetSecretAsync(string name)
    {
        var value = _config[name];
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"Secret \"{name}\" is not set. Check environment variables or appsettings.");
        return Task.FromResult(value);
    }
}
