using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scribal.Cli;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<InterfaceManager>();

var app = builder.Build();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();
