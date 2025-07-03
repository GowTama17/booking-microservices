using System.Threading.RateLimiting;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.Exception;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.Jwt;
using BuildingBlocks.Logging;
using BuildingBlocks.Mapster;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Passenger.Data;
using Passenger.GrpcServer.Services;
using Serilog;

namespace Passenger.Extensions.Infrastructure;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddInfrastructure(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var env = builder.Environment;

        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddScoped<IEventMapper, PassengerEventMapper>();
        builder.Services.AddScoped<IEventDispatcher, EventDispatcher>();

        builder.Services.Configure<ApiBehaviorOptions>(
            options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.Services.AddRateLimiter(
            options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                    httpContext =>
                        RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: httpContext.User.Identity?.Name ??
                                          httpContext.Request.Headers.Host.ToString(),
                            factory: partition => new FixedWindowRateLimiterOptions
                            {
                                AutoReplenishment = true,
                                PermitLimit = 10,
                                QueueLimit = 0,
                                Window = TimeSpan.FromMinutes(1)
                            }));
            });

        builder.Services.AddPersistMessageProcessor();
        builder.AddCustomDbContext<PassengerDbContext>();
        builder.AddMongoDbContext<PassengerReadDbContext>();

        builder.AddCustomSerilog(env);
        builder.Services.AddJwt();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddCustomMediatR();
        builder.Services.AddValidatorsFromAssembly(typeof(PassengerRoot).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddCustomMapster(typeof(PassengerRoot).Assembly);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddCustomHealthCheck();
        builder.Services.AddCustomMassTransit(env, TransportType.RabbitMq, typeof(PassengerRoot).Assembly);
        builder.AddCustomObservability();

        builder.Services.AddGrpc(
            options =>
            {
                options.Interceptors.Add<GrpcExceptionInterceptor>();
            });

        return builder;
    }


    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        var env = app.Environment;
        var appOptions = app.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseCustomObservability();

        app.UseCustomProblemDetails();

        app.UseSerilogRequestLogging(
            options =>
            {
                options.EnrichDiagnosticContext = LogEnrichHelper.EnrichFromRequest;
            });

        app.UseCorrelationId();
        app.UseMigration<PassengerDbContext>();
        app.UseCustomHealthCheck();
        app.MapGrpcService<PassengerGrpcServices>();
        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

        if (env.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
