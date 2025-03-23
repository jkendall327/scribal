using Scribal.Cli;
using Spectre.Console;

// Dictionary of available commands

var manager = new InterfaceManager();

await manager.DisplayWelcome();
await manager.RunMainLoop();
