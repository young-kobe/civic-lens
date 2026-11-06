namespace CivicLens.Application.Analysis;

public sealed record AnalysisCurrentVersions(string TaskVersion, IReadOnlyList<string> PromptVersions, IReadOnlyList<string> SchemaVersions)
{
    public static AnalysisCurrentVersions Of(AnalysisTaskIdentity identity) =>
        new(identity.TaskVersion, [identity.PromptVersion], [identity.SchemaVersion]);

    public bool IsCurrent(string taskVersion, string promptVersion, string schemaVersion) =>
        taskVersion == TaskVersion && PromptVersions.Contains(promptVersion) && SchemaVersions.Contains(schemaVersion);
}
