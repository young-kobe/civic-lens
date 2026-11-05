using CivicLens.Application.People;
using CivicLens.Core.Review;

namespace CivicLens.Host.People;

internal static class PeopleCommand
{
    private const string NoteOption = "--note";

    private const string Usage = "Use people import <config.json>, people photo withhold|restore <person-id> [--note <text>], " +
        "people website set <person-id> <url> [--note <text>], or people website clear <person-id> [--note <text>].";

    public static bool Matches(string[] args) => args is ["people", ..];

    public static Task<int> ExecuteAsync(string[] args, IPersonRegistryStore people, ReviewActor owner, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            var (words, note) = SplitNote(args);
            var settings = new ChangePersonSetting(people);
            object? result = words switch
            {
                ["people", "import", var path] when note is null =>
                    await people.ImportAsync(ConfigurationFile.ReadCollection(path).People, owner.Subject, cancellationToken),
                ["people", "photo", "withhold", var id] =>
                    await settings.WithholdPhotoAsync(owner, id, note ?? "Photo withheld by the owner.", cancellationToken),
                ["people", "photo", "restore", var id] =>
                    await settings.RestorePhotoAsync(owner, id, note ?? "Photo restored by the owner.", cancellationToken),
                ["people", "website", "set", var id, var url] =>
                    await settings.SetWebsiteAsync(owner, id, url, note ?? "Website set by the owner.", cancellationToken),
                ["people", "website", "clear", var id] =>
                    await settings.ClearWebsiteAsync(owner, id, note ?? "Website cleared by the owner.", cancellationToken),
                _ => null
            };
            if (result is null)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            CommandOutput.Write(result);
            return 0;
        });

    // The note is the last two arguments. A note option anywhere else makes a bad call.
    private static (string[] Words, string? Note) SplitNote(string[] args) =>
        args is [.., NoteOption, var note] ? (args[..^2], note) : (args, null);
}
