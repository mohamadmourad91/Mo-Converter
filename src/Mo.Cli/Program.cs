// mo list <files...>                       what can be done with these files, per wheel
// mo run <operation-id> <files...> [--out <folder>]
// mo help | mo version

using Mo.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;
return await Cli.RunAsync(args);
