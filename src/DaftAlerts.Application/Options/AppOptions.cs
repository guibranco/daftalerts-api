using System;

namespace DaftAlerts.Application.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string ApiToken { get; set; } = string.Empty;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";
    public int RawEmailDays { get; set; } = 90;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public bool AutoMigrate { get; set; } = true;
}
