namespace CivicLens.Application;

public static class FoundationStatus
{
    public const string Description = "Watched-page tracer: v1/v2 configuration validation with dated coverage and names, bounded HTTP collection, immutable gzip captures, and verified receipts. Explicit database migrations, atomic collection/import, and saved receipt recovery without recollection are available through the CLI. Managed durable jobs provide bounded retries, recovery, and shared origin pacing; a sequential polling worker dispatches eligible jobs. The authenticated review workspace lets the owner enqueue or cancel collection, admit discovered articles, extract text, compare versions, and recollect retained article URLs under current settings. Version 7 collection applies tolerant HTML recovery while preserving older protocol behavior; capture-text-v2 bounds extraction and parses malformed HTML. Immutable HTML/plain-text extraction, reusable versioned HTML content profiles, retained processing versions, and exact text-span citations remain available. Semantic document changes, AI, publication, and MCP are not implemented.";
}
