using CivicLens.Application;

if (args is [] or ["--help"] or ["help"])
{
    Console.WriteLine("Civic Lens: civic-lens status");
    return 0;
}

if (args is ["status"])
{
    Console.WriteLine(FoundationStatus.Description);
    return 0;
}

Console.Error.WriteLine("Unknown command. Use --help.");
return 2;
