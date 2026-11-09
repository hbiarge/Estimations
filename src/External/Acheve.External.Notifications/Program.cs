var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthorization();

app.MapControllers();

app.Run();
