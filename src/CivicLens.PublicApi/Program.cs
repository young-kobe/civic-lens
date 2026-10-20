using CivicLens.PublicApi;

try
{
    await PublicApiApp.Build(PublicApiOptions.FromEnvironment(), args).RunAsync();
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or FormatException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
