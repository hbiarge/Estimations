using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseFileServer(new FileServerOptions
{
    RequestPath = "/images",
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "Images"))
});

app.MapGet("/", () => "Download images from here!");

app.Run();
