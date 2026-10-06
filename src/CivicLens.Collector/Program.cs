if (args is [] or ["--help"] or ["help"])
{
    Console.WriteLine("Civic Lens collector: bounded source collection executable.");
    Console.WriteLine("Collection is not implemented. The JSON/JSONL protocol arrives in phase 1.");
    return 0;
}

Console.Error.WriteLine("Collection is not implemented. Use --help.");
return 2;
