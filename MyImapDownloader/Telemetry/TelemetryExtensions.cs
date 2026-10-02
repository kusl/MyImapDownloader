using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Telemetry;

public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddCoreTelemetry(
            configuration,
            DiagnosticsConfig.ServiceName,
            DiagnosticsConfig.ServiceVersion);
}
