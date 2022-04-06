using System.Diagnostics;
using AzureVote;
using AzureVote.Data;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<VoteService>();

// Add Redis service
var redisConnection = ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("RedisHost"));
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

// Application settings
builder.Services.Configure<VoteAppSettings>(builder.Configuration.GetSection(nameof(VoteAppSettings)));

// Optional: Configure tracing
builder.Services.AddOpenTelemetryTracing(builder => builder
    // Customize the traces gathered by the HTTP request handler
    .AddAspNetCoreInstrumentation(options =>
    {
        options.RecordException = true;
        // Add metadata for the request such as the HTTP method and response length
        options.Enrich = (activity, eventName, rawObject) =>
        {
            switch (eventName)
            {
                case "OnStartActivity":
                    {
                        if (rawObject is not HttpRequest httpRequest)
                        {
                            return;
                        }

                        activity.SetTag("requestProtocol", httpRequest.Protocol);
                        activity.SetTag("requestMethod", httpRequest.Method);
                        break;
                    }
                case "OnStopActivity":
                    {
                        if (rawObject is HttpResponse httpResponse)
                        {
                            activity.SetTag("responseLength", httpResponse.ContentLength);
                        }

                        break;
                    }
            }
        };
    })
    // Instrument Redis client
    .AddRedisInstrumentation(redisConnection, opt =>
    {
        opt.FlushInterval = TimeSpan.FromSeconds(1);
        opt.SetVerboseDatabaseStatements = true;
    })
    .AddSource("my-corp.azure-vote.vote-app")
    // Create resources (key-value pairs) that describe your service such as service name and version
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("vote-app")
        .AddAttributes(new[] { new KeyValuePair<string, object>("service.version", "1.0.0.0") }))
    // Ensures that all activities are recorded and sent to exporter
    .SetSampler(new AlwaysOnSampler())
    .AddConsoleExporter()
// Exports spans to Otlp endpoint
// .AddOtlpExporter(otlpOptions =>
// {
//     otlpOptions.Endpoint = new Uri("http://localhost:4318/v1/traces");
//     otlpOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
// })
);

builder.Services.AddSingleton(new ActivitySource("my-corp.azure-vote.vote-app"));


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// Optional: Add metrics
app.UseMetricServer();

app.MapGet("/traced-exception/", (ActivitySource activitySource) =>
    {
        using var activity = activitySource.StartActivity("Error endpoint", ActivityKind.Server);
        throw new ApplicationException("Error processing the request");
    })
    .WithName("TracedException")
    .Produces<ApplicationException>()
    .Produces(StatusCodes.Status500InternalServerError);

app.Run();