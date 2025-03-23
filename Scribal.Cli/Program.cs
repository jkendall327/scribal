using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Scribal.Cli;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<InterfaceManager>();
builder.Services.AddSingleton<IModelClient, ModelClient>();

var key = builder.Configuration["OPENAI_API_KEY"];
builder.Services.AddSingleton(new OpenAIClient(key));

builder.Services
    .AddChatClient(services => services.GetRequiredService<OpenAIClient>().AsChatClient("gpt-4o-mini"))
    .UseLogging()
    .UseFunctionInvocation();

var app = builder.Build();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();
