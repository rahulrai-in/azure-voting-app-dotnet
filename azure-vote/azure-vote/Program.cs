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

// Configure OpenTelemetry tracing
builder.Services.AddOpenTelemetryTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
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
            // Displays traces on the console. Useful for debugging.
            .AddConsoleExporter();
        if (builder.Configuration.GetValue<bool>("EnableOtlpExporter"))
        {
            // Exports spans to an OTLP endpoint. Use this for exporting traces to collector or a backend that support OTLP over HTTP
            tracerProviderBuilder.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new("http://localhost:4318/v1/traces");
                otlpOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }
    }
);

builder.Services.AddSingleton(new ActivitySource("my-corp.azure-vote.vote-app"));

// Configure OpenTelemetry metrics
builder.Services.AddOpenTelemetryMetrics(meterProviderBuilder =>
    {
        meterProviderBuilder.AddAspNetCoreInstrumentation()
            .AddMeter("my-corp.azure-vote.vote-app")
            // Create resources (key-value pairs) that describe your service such as service name and version
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault().AddService("vote-app")
                    .AddAttributes(new[] { new KeyValuePair<string, object>("service.version", "1.0.0.0") }))
            // Displays metrics on the console. Useful for debugging.
            .AddConsoleExporter();
        if (builder.Configuration.GetValue<bool>("EnableOtlpExporter"))
        {
            // Exports metrics to an OTLP endpoint. Use this for exporting metrics to collector or a backend that support OTLP over HTTP
            meterProviderBuilder.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new("http://localhost:4318/v1/metrics");
                otlpOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        }
    }
);

builder.Services.AddSingleton(new Meter("my-corp.azure-vote.vote-app"));

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

app.MapGet("/traced-exception/", (ActivitySource activitySource) =>
    {
        using var activity = activitySource.StartActivity("Error endpoint", ActivityKind.Server);
        throw new ApplicationException("Error processing the request");
    })
    .WithName("TracedException")
    .Produces<ApplicationException>()
    .Produces(StatusCodes.Status500InternalServerError);

app.Run();