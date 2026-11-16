using System.Threading.Channels;

using Microsoft.Extensions.FileProviders;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure;
using QualityVision.Web.Components;
using QualityVision.Web.Endpoints;
using QualityVision.Web.Pipeline;

var builder = WebApplication.CreateBuilder(args);

// Optional, git-ignored machine-local settings: the HTTPS endpoint (Kestrel:Endpoints:Https) with certificate
// paths outside the repo. Environment variables and command-line args are re-added afterwards so they still
// win over the local file (run-demo.sh can override anything).
// rules.json (ADR-002) comes after appsettings*.json. It is read by absolute path from the content root (the project
// folder under `dotnet run`, not the bin copy), so the file watcher watches the file people actually edit.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddJsonFile(source =>
    {
        source.Path = "rules.json";
        source.FileProvider = new PhysicalFileProvider(builder.Environment.ContentRootPath);
        source.Optional = false;
        source.ReloadOnChange = true;
        // A syntax error on reload must not go unnoticed (it would end in an unobserved watcher task): log it and
        // keep running; the provider then keeps the last valid rules.
        source.OnLoadException = ctx =>
        {
            using ILoggerFactory loggerFactory = LoggerFactory.Create(static b => b.AddSimpleConsole());
            loggerFactory.CreateLogger("rules.json")
                .LogWarning(ctx.Exception, "rules.json could not be read, keeping the previous rules");
            ctx.Ignore = true;
        };
    })
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHealthChecks()
    .AddCheck<ModelHealthCheck>("model");

// Infrastructure: CameraOptions, TimeProvider, frame sources, YOLO detector, Skia renderer.
builder.Services.AddQualityVisionInfrastructure(builder.Configuration);

builder.Services.AddOptions<StreamOptions>()
    .BindConfiguration(StreamOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<InspectionOptions>()
    .BindConfiguration(InspectionOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Capture → detection hand-off: capacity 2, DropOldest. Dropped frames are disposed so their pooled
// buffers go back to the pool; latency and memory stay bounded even when detection is slower than capture.
builder.Services.AddSingleton<Channel<Frame>>(_ => Channel.CreateBounded<Frame>(
    new BoundedChannelOptions(2)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true,
    },
    static dropped => dropped.Dispose()));

builder.Services.AddSingleton<CameraStatus>();
builder.Services.AddSingleton<ICameraStatus>(static sp => sp.GetRequiredService<CameraStatus>());
builder.Services.AddSingleton<PipelineMetrics>();
builder.Services.AddSingleton<IPipelineMetrics>(static sp => sp.GetRequiredService<PipelineMetrics>());
builder.Services.AddSingleton<FrameBroadcaster>();
builder.Services.AddSingleton<IInspectionLog, InspectionLog>();
builder.Services.AddSingleton<FrameUploadGate>();
builder.Services.AddSingleton<RulesFileService>();
builder.Services.AddHostedService<CameraService>();
builder.Services.AddHostedService<DetectionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Kestrel:Endpoints: plain HTTP on :5080 for the dashboard and /stream (no HTTPS redirection);
// HTTPS on :5443 (appsettings.Local.json) only for the phone capture page and its frame endpoint.

app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapStaticAssets();
app.MapCameraEndpoints();
app.MapPipelineEndpoints();
app.MapInspectionEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
