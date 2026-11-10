using Acheve.Common.Chaos;
using Acheve.External.Shared;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Like a real external system: its traces stay apart from the application's traces.
builder.AddExternalSystemDefaults();

// Faults and latency switched at run time (/chaos), for the downloads too.
builder.AddChaos();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseChaos();

app.UseFileServer(new FileServerOptions
{
    RequestPath = "/images",
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "Images"))
});

app.MapGet("/", () => "Download images from here!");

app.Run();
