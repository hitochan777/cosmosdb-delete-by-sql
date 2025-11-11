using Spectre.Console.Cli;

var app = new CommandApp<DeleteCommand>();
return await app.RunAsync(args);
