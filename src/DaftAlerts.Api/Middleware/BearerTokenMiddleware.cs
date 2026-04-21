using System;
using System.Threading.Tasks;
using DaftAlerts.Application.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DaftAlerts.Api.Middleware;

/// <summary>
/// Minimal bearer-token auth. The token is a single string configured at <c>Auth:ApiToken</c>.
/// Applied only to paths under <c>/api</c>; <c>/swagger</c> and <c>/health</c> are left open.
/// </summary>
public sealed class BearerTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthOptions _options;
    private readonly ILogger<BearerTokenMiddleware> _logger;

    public BearerTokenMiddleware(
        RequestDelegate next,
        IOptions<AuthOptions> options,
        ILogger<BearerTokenMiddleware> logger
    )
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            _logger.LogError("Auth:ApiToken is not configured. Refusing /api/* requests.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var header))
        {
            await WriteUnauthorizedAsync(context, "Missing Authorization header.");
            return;
        }

        var value = header.ToString();
        const string prefix = "Bearer ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(
                context,
                "Authorization header must use the Bearer scheme."
            );
            return;
        }

        var token = value[prefix.Length..].Trim();
        if (!CryptographicEquals(token, _options.ApiToken))
        {
            await WriteUnauthorizedAsync(context, "Invalid token.");
            return;
        }

        await _next(context);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401,
                detail,
            }
        );
    }
}
