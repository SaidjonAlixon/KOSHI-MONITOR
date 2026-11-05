using KOSHI.Application.Abstractions;
using KOSHI.Infrastructure.Persistence;
using KOSHI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KOSHI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKoshiInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Koshi");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Koshi is not configured (set KOSHI_ConnectionStrings__Koshi).");

        services.AddDbContext<KoshiDbContext>(o => o.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(KoshiDbContext).Assembly.FullName);
            npgsql.EnableRetryOnFailure(3);
        }));
        services.AddScoped<IKoshiDbContext>(sp => sp.GetRequiredService<KoshiDbContext>());

        var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        jwt.CreateKey();
        services.AddSingleton(jwt);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISecretBox, AesSecretBox>();

        var iterations = config.GetValue("Security:PasswordHashIterations", Pbkdf2PasswordHasher.DefaultIterations);
        services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher(iterations));
        services.AddSingleton<IDbErrorClassifier, PostgresErrorClassifier>();

        services.AddSingleton(config.GetSection(Agent.AgentReleaseOptions.Section).Get<Agent.AgentReleaseOptions>() ?? new Agent.AgentReleaseOptions());
        services.AddScoped<Agent.AgentReleaseService>();
        services.AddSingleton(config.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions());
        services.AddSingleton(config.GetSection(BootstrapOptions.Section).Get<BootstrapOptions>() ?? new BootstrapOptions());
        services.AddScoped<DatabaseInitializer>();
        return services;
    }
}

public sealed class PostgresErrorClassifier : IDbErrorClassifier
{
    public bool IsUniqueViolation(Exception exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } ||
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
