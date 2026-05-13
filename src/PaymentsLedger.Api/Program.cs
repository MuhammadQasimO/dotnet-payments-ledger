using System.Reflection;

using HealthChecks.UI.Client;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using PaymentsLedger.Api.Middleware;
using PaymentsLedger.Application;
using PaymentsLedger.Infrastructure;
using PaymentsLedger.Infrastructure.Persistence;

using Prometheus;

using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

const string ServiceName = "payments-ledger";

var bootstrapLogger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();
Log.Logger = bootstrapLogger;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // -------- Serilog --------
    builder.Host.UseSerilog((ctx, sp, cfg) =>
    {
        cfg.MinimumLevel.Information()
           .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
           .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
           .Enrich.FromLogContext()
           .Enrich.WithMachineName()
           .Enrich.WithProperty("service", ServiceName)
           .ReadFrom.Configuration(ctx.Configuration);

        if (ctx.HostingEnvironment.IsDevelopment())
        {
            cfg.WriteTo.Console();
        }
        else
        {
            cfg.WriteTo.Console(new RenderedCompactJsonFormatter());
        }
    });

    // -------- DI --------
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        var xml = Path.Combine(AppContext.BaseDirectory,
            $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
        if (File.Exists(xml))
        {
            opts.IncludeXmlComments(xml);
        }
    });

    builder.Services.AddProblemDetails();

    // -------- Health checks --------
    var dbConn = builder.Configuration.GetConnectionString("Ledger")!;
    var redisConn = builder.Configuration.GetConnectionString("Redis")!;
    builder.Services.AddHealthChecks()
        .AddNpgSql(dbConn, name: "postgres", tags: new[] { "ready" })
        .AddRedis(redisConn, name: "redis", tags: new[] { "ready" });

    // -------- OpenTelemetry --------
    var otelEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r
            .AddService(ServiceName, serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString()))
        .WithTracing(t =>
        {
            t.AddAspNetCoreInstrumentation()
             .AddHttpClientInstrumentation()
             .AddEntityFrameworkCoreInstrumentation()
             .AddSource("Npgsql"); // surface Npgsql ActivitySource without the AddNpgsql() shim
            if (!string.IsNullOrWhiteSpace(otelEndpoint))
            {
                t.AddOtlpExporter(opt => opt.Endpoint = new Uri(otelEndpoint));
            }
        });

    var app = builder.Build();

    // -------- Pipeline --------
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseSerilogRequestLogging();

    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<CorrelationMiddleware>();
    app.UseMiddleware<UserIdMiddleware>();
    app.UseMiddleware<RateLimitMiddleware>();
    app.UseMiddleware<IdempotencyMiddleware>();

    app.UseHttpMetrics();
    app.MapMetrics("/metrics");

    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false, // liveness: process is up
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = h => h.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    });

    app.MapControllers();

    // -------- Migrations on startup (dev/test only) --------
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("AutoMigrate"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await db.Database.MigrateAsync();
    }

    Log.Information("Starting {Service}", ServiceName);
    await app.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Expose Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;
