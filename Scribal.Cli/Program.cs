using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Scribal.Cli;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<InterfaceManager>();

var environmentVariable = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
builder.Services.AddSingleton(new OpenAIClient(environmentVariable ?? "fake"));

builder.Services
    .AddChatClient(services => services.GetRequiredService<OpenAIClient>().AsChatClient("gpt-4o-mini"))
    .UseDistributedCache()
    .UseLogging()
    .UseFunctionInvocation();

var app = builder.Build();

var manager = app.Services.GetRequiredService<InterfaceManager>();

await manager.DisplayWelcome();
await manager.RunMainLoop();
