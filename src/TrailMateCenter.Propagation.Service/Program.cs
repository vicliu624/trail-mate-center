using Microsoft.AspNetCore.Server.Kestrel.Core;
using TrailMateCenter.Propagation.Engine;
using TrailMateCenter.Propagation.Service.Runtime;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(51051, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc();
builder.Services.AddSingleton<IPropagationSolver, FormalPropagationSolver>();
builder.Services.AddSingleton<PropagationJobStore>();

var app = builder.Build();

app.MapGrpcService<PropagationGrpcService>();
app.Run();
