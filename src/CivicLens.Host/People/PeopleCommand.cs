using CivicLens.Application.People;

namespace CivicLens.Host.People;

internal static class PeopleCommand
{
    private const string Usage = "Use people import <config.json>.";

    public static bool Matches(string[] args) => args is ["people", ..];

    public static Task<int> ExecuteAsync(string[] args, IPersonRegistryStore people, string actorSubject, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            if (args is not ["people", "import", var path])
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            CommandOutput.Write(await people.ImportAsync(ConfigurationFile.ReadCollection(path).People, actorSubject, cancellationToken));
            return 0;
        });
}
