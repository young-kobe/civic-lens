using System.Reflection;
using CivicLens.Application.Publication;
using CivicLens.Host.Actions;
using CivicLens.Host.Briefs;
using CivicLens.Host.Documents;
using CivicLens.Host.ExecutiveActions;
using CivicLens.Host.Relationships;
using CivicLens.Host.Review;
using CivicLens.Host.Statements;
using CivicLens.Host.Votes;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;

namespace CivicLens.Tests.Host;

public sealed class TakeOffSiteWiringTests
{
    public static TheoryData<Type, PublishedRecordKind, string, string> Editors => new()
    {
        { typeof(DraftEditor), PublishedRecordKind.DocumentChange, "DraftId", "/Review/{id}" },
        { typeof(StatementEditor), PublishedRecordKind.Statement, "CandidateId", "/Review/Statements/{id}" },
        { typeof(ActionEditor), PublishedRecordKind.Action, "RecordId", "/Review/Actions/{id}" },
        { typeof(VoteEditor), PublishedRecordKind.Vote, "RecordId", "/Review/Votes/{id}" },
        { typeof(RelationshipEditor), PublishedRecordKind.Relationship, "RecordId", "/Review/Relationships/{id}" },
        { typeof(BriefEditor), PublishedRecordKind.Brief, "RecordId", "/Review/Briefs/{id}" },
        { typeof(ExecutiveActionEditor), PublishedRecordKind.ExecutiveAction, "RecordId", "/Review/ExecutiveActions/{id}" }
    };

    [Fact]
    public void EveryPublishedKindHasAnEditorThatTakesItOffSoNoKindCanBePublishedWithoutTheAction()
    {
        var wired = Editors.Select(row => (PublishedRecordKind)row[1]).ToHashSet();

        Assert.Equal(Enum.GetValues<PublishedRecordKind>().ToHashSet(), wired);
    }

    [Theory]
    [MemberData(nameof(Editors))]
    public void AnEditorNamesItsOwnKindAndRecordIdAndTheDoneRedirectLandsOnItsOwnPage(Type editor, PublishedRecordKind kind, string idParameter, string route)
    {
        var page = (PublishedRecordEditorPage)Activator.CreateInstance(editor)!;
        editor.GetProperty(idParameter)!.SetValue(page, "the-id");

        var record = typeof(PublishedRecordEditorPage).GetProperty("TakeOffRecord", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
        Assert.Equal(new PublishedRecordReference(kind, "the-id"), record);
        var template = editor.GetCustomAttribute<RouteAttribute>()!.Template;
        Assert.Equal(route.Replace("{id}", "{" + idParameter + "}"), template);
        var routes = typeof(PublishedRecordEditorPage).Assembly.GetType("CivicLens.Host.Review.RecordRoutes")!;
        var target = (string)routes.GetMethod("Editor")!.Invoke(null, [kind, "the-id", TakeOffSitePanel.DoneStatus])!;
        Assert.Equal(route.Replace("{id}", "the-id") + "?status=" + TakeOffSitePanel.DoneStatus, target);
    }
}
