using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace KOSHI.Api.Infrastructure;

/// <summary>Adds the admin Bearer and agent Device security schemes to the OpenAPI document.</summary>
public sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "KOSHI API";
        document.Info.Description = "KOSHI Enterprise Windows monitoring platform. Admin endpoints use Bearer JWT; agent endpoints use 'Authorization: Device <credential>'.";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Admin access token from POST /api/auth/login",
        };
        document.Components.SecuritySchemes["Device"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "Agent credential: 'Device kd1.<id>.<secret>'",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
