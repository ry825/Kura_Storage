const string help = """
KuraStorage administration CLI

Usage:
  kurastorage-admin [command] [options]

Commands:
  help    Show this help text.
""";

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    Console.WriteLine(help);
    return 0;
}

Console.Error.WriteLine($"Unknown command: {args[0]}");
Console.Error.WriteLine("Run with --help to list available commands.");
return 2;
