using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class AiTextTests
{
    [Theory]
    [InlineData("_Bill")]
    [InlineData("2Bill")]
    [InlineData("Bill2")]
    [InlineData("\u0301Bill")]
    [InlineData("Bill\u0301")]
    [InlineData("\U00010400Bill")]
    [InlineData("billing")]
    public async Task AGlossaryNameInsideAUnicodeWordStaysPlainText(string text)
    {
        var document = await RenderAsync(AiTextContent(text));

        Assert.Equal(text, document.Body!.TextContent);
        Assert.Empty(document.QuerySelectorAll("button.term"));
        Assert.Empty(document.QuerySelectorAll("[popover]"));
    }

    [Fact]
    public async Task ModelTextKeepsItsCasingAndEncodesHtmlPayloads()
    {
        const string text = "bIlL <img src=x onerror=alert(1)>";
        var document = await RenderAsync(AiTextContent(text));

        Assert.Equal(text, VisibleText(document.Body!));
        Assert.Equal("bIlL", document.QuerySelector("button.term")!.TextContent);
        Assert.Empty(document.QuerySelectorAll("img"));
        Assert.Equal("term-bill", document.QuerySelector("[popover]")!.Id);
    }

    [Theory]
    [InlineData(PublishedTextOrigin.Code)]
    [InlineData(PublishedTextOrigin.Person)]
    public async Task NonModelTextDoesNotReceiveGlossaryMarkup(PublishedTextOrigin origin)
    {
        const string text = "Bill";
        var document = await RenderAsync(AiTextContent(text, origin));

        Assert.Equal(text, document.Body!.TextContent);
        Assert.Empty(document.QuerySelectorAll("button.term"));
        Assert.Empty(document.QuerySelectorAll("[popover]"));
    }

    [Fact]
    public async Task ManualTermsAndSeveralAiTextComponentsShareThePageFirstUseRegistry()
    {
        var content = new RenderFragment(builder =>
        {
            builder.OpenComponent<Term>(0);
            builder.AddAttribute(1, nameof(Term.Key), "bill");
            builder.AddAttribute(2, nameof(Term.ChildContent), (RenderFragment)(child => child.AddContent(0, "Bill")));
            builder.CloseComponent();
            builder.AddContent(3, AiTextContent("Bill"));
            builder.AddContent(4, AiTextContent("BILL"));
        });

        var document = await RenderAsync(content);

        Assert.Equal("BillBillBILL", VisibleText(document.Body!));
        Assert.Single(document.QuerySelectorAll("button.term"));
        Assert.Single(document.QuerySelectorAll("[popover]"));
        Assert.Equal("term-bill", document.QuerySelector("button.term")!.GetAttribute("popovertarget"));
        Assert.Equal("term-bill", document.QuerySelector("[popover]")!.Id);
    }

    [Fact]
    public async Task AnEarlierAiTextTermOwnsFirstUseBeforeALaterManualTerm()
    {
        var document = await RenderAsync(builder =>
        {
            builder.AddContent(0, AiTextContent("bIlL"));
            builder.OpenComponent<Term>(3);
            builder.AddAttribute(4, nameof(Term.Key), "bill");
            builder.AddAttribute(5, nameof(Term.ChildContent), (RenderFragment)(child => child.AddContent(0, "BILL")));
            builder.CloseComponent();
        });

        Assert.Equal("bIlLBILL", VisibleText(document.Body!));
        Assert.Single(document.QuerySelectorAll("button.term"));
        Assert.Equal("bIlL", document.QuerySelector("button.term")!.TextContent);
    }

    private static RenderFragment AiTextContent(string text, PublishedTextOrigin origin = PublishedTextOrigin.Model) => builder =>
    {
        builder.OpenComponent<AiText>(0);
        builder.AddAttribute(1, nameof(AiText.Text), text);
        builder.AddAttribute(2, nameof(AiText.Origin), origin);
        builder.CloseComponent();
    };

    private static async Task<IDocument> RenderAsync(RenderFragment content)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CascadingValue<GlossaryPageUse>>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CascadingValue<GlossaryPageUse>.Value)] = new GlossaryPageUse(),
                [nameof(CascadingValue<GlossaryPageUse>.IsFixed)] = true,
                [nameof(CascadingValue<GlossaryPageUse>.ChildContent)] = content
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    private static string VisibleText(IElement element) => string.Concat(element.ChildNodes
        .Where(node => node is not IElement child || !child.HasAttribute("popover"))
        .Select(node => node.TextContent));
}
