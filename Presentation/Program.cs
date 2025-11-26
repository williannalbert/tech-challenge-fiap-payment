using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Application.Interfaces.Event;
using Application.Interfaces.MessageBus;
using Application.Interfaces.Services;
using Application.Services;
using Domain.Interfaces.Repositories;
using Infrastructure.Data.EventSourcing;
using Infrastructure.Data.Repositories;
using Infrastructure.Logging;
using Infrastructure.MessageBus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Presentation.Middlewares;
using Serilog;
using System.Collections.Specialized;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var awsOptions = builder.Configuration.GetAWSOptions();
builder.Services.AddDefaultAWSOptions(awsOptions);

builder.Services.AddDbContext<EventStoreDbContext>(options =>
    options.UseNpgsql(connectionString));


builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("PaymentService")
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(
                        serviceName: builder.Configuration.GetSection("Serilog:Properties:ServiceName").Value ?? "PaymentService",
                        serviceVersion: "1.0.0"))
            .AddHttpClientInstrumentation()
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true;
            })
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(builder.Configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317");
            });
    });


Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .CreateBootstrapLogger();

builder.Host.UseSerilog(
    (context, services, configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("X-Correlation-ID", context.HostingEnvironment.ApplicationName)
            .WriteTo.Console()
        );

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PaymentService API", Version = "v1" });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddAWSService<IAmazonSimpleNotificationService>();
builder.Services.AddAWSService<IAmazonSQS>();

builder.Services.AddScoped<ICommandPublisher, SqsCommandPublisher>();

builder.Services.AddScoped<IEventStoreUnitOfWork, EventStoreUnitOfWork>();

builder.Services.AddScoped<IWalletRepository, EfWalletRepository>();
builder.Services.AddScoped<IPurchaseRepository, EfPurchaseRepository>();

builder.Services.AddScoped<IWalletApplicationService, WalletApplicationService>();
builder.Services.AddScoped<IPurchaseApplicationService, PurchaseApplicationService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "AllowAllOrigins",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "PostgreSQL");


var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PaymentService API V1");
        c.RoutePrefix = "swagger"; 
    });
}

app.UseHttpsRedirection();

app.UseCors("AllowAllOrigins");
app.UseMiddleware<JwtMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var options = new JsonSerializerOptions { WriteIndented = true };

        var response = new
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration.TotalMilliseconds,
            Checks = report.Entries.Select(entry => new
            {
                Name = entry.Key,
                Status = entry.Value.Status.ToString(),
                entry.Value.Description,
                Duration = entry.Value.Duration.TotalMilliseconds,
                entry.Value.Data,
            }),
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response, options)
        );
    },
});

app.Run();
