using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CivicLens.Collection.Contracts;

namespace CivicLens.Collector;

public sealed class HttpCollector : IDisposable
{
    private readonly HttpClient client;

    public HttpCollector(HttpMessageHandler? handler = null)
    {
        var actual = handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            MaxResponseDrainSize = 0
        };
        client = new HttpClient(actual, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<CollectionResult> FetchAsync(CollectionRequest request, CancellationToken cancellationToken = default)
    {
        var requests = 0;
        long bytes = 0;
        var current = Uri.TryCreate(request.Url, UriKind.Absolute, out var parsed) ? parsed : new Uri("http://invalid/");
        var requestedUri = current;
        int? status = null;
        try
        {
            request.Validate();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
            var token = timeout.Token;
            RobotsRules rules;
            try { rules = await GetRobotsAsync(request, () => requests++, n => bytes += n, () => bytes, token); }
            catch (RobotsException) { return Result(CollectionOutcome.Failed, "robotsUnavailable"); }
            catch (HttpRequestException) { return Result(CollectionOutcome.Failed, "robotsUnavailable"); }
            if (!rules.Allowed(current.PathAndQuery, token)) return Result(CollectionOutcome.Failed, "robotsDenied");
            await Delay(request.MinDelayMilliseconds, token);
            var target = current;
            while (true)
            {
                EnsureBudget(request, requests);
                if (!request.Allows(target)) return Result(CollectionOutcome.Failed, "outOfScope");
                using var message = new HttpRequestMessage(HttpMethod.Get, target);
                message.Headers.UserAgent.ParseAdd("CivicLens/0.1");
                if (target == requestedUri)
                {
                    if (request.ETag is not null) message.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(request.ETag));
                    if (request.LastModified is not null) message.Headers.IfModifiedSince = request.LastModified;
                }
                var hasConditionals = message.Headers.IfNoneMatch.Count > 0 || message.Headers.IfModifiedSince is not null;
                requests++;
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
                status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    if (!hasConditionals) return Result(CollectionOutcome.Failed, "unexpectedNotModified");
                    return Result(CollectionOutcome.NotModified, etag: response.Headers.ETag?.ToString(), modified: response.Content.Headers.LastModified);
                }
                if ((int)response.StatusCode == 429)
                    return Result(CollectionOutcome.Deferred, "rateLimited", retry: RetrySeconds(response.Headers.RetryAfter));
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (response.Headers.Location is null) return Result(CollectionOutcome.Failed, "redirectMissingLocation");
                    var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(target, response.Headers.Location);
                    if (!request.Allows(next)) return Result(CollectionOutcome.Failed, "outOfScope");
                    if (!rules.Allowed(next.PathAndQuery, token)) return Result(CollectionOutcome.Failed, "robotsDenied");
                    await Delay(request.MinDelayMilliseconds, token);
                    target = next; current = target;
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) return Result(CollectionOutcome.Failed, "httpError");
                var artifact = await SaveBoundedAsync(response, request, () => bytes, n => bytes += n, token);
                var etag = response.Headers.ETag?.ToString();
                DateTimeOffset? modified = response.Content.Headers.LastModified;
                return Result(CollectionOutcome.Captured, artifactPath: artifact.Path, etag: etag, modified: modified,
                    artifactBytes: artifact.Bytes, sha256: artifact.Hash);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Result(CollectionOutcome.Failed, "cancelled"); }
        catch (OperationCanceledException) { return Result(CollectionOutcome.Failed, "timeout"); }
        catch (BudgetException e) { return Result(CollectionOutcome.Failed, e.Code); }
        catch (RobotsException) { return Result(CollectionOutcome.Failed, "robotsUnavailable"); }
        catch (HttpRequestException) { return Result(CollectionOutcome.Failed, "transportError"); }
        catch (UnauthorizedAccessException) { return Result(CollectionOutcome.Failed, "artifactWriteFailed"); }
        catch (IOException) { return Result(CollectionOutcome.Failed, "incompleteResponse"); }
        catch (ArgumentException) { return Result(CollectionOutcome.Failed, "invalidRequest"); }

        CollectionResult Result(CollectionOutcome outcome, string? failure = null, string? artifactPath = null,
            string? etag = null, DateTimeOffset? modified = null, long? retry = null, long? artifactBytes = null, string? sha256 = null) => new()
            {
                JobId = request.JobId,
                SourceId = request.SourceId,
                RequestedUrl = request.Url,
                FinalUrl = current.ToString(),
                Outcome = outcome,
                ObservedAt = DateTimeOffset.UtcNow,
                HttpStatus = status,
                ETag = etag,
                LastModified = modified,
                Sha256 = sha256,
                ArtifactBytes = artifactBytes,
                ArtifactPath = artifactPath,
                BytesReceived = bytes,
                RequestCount = requests,
                FailureCode = outcome is CollectionOutcome.Failed or CollectionOutcome.Deferred ? failure : null,
                RetryAfterSeconds = retry
            };
    }

    private async Task<RobotsRules> GetRobotsAsync(CollectionRequest request, Action count, Action<long> addBytes, Func<long> getBytes, CancellationToken token)
    {
        var robotUri = new Uri(new Uri(request.AllowedOrigin), "/robots.txt");
        count();
        using var robotRequest = new HttpRequestMessage(HttpMethod.Get, robotUri);
        robotRequest.Headers.UserAgent.ParseAdd("CivicLens/0.1");
        using var response = await client.SendAsync(robotRequest, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return new RobotsRules();
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))) throw new RobotsException();
        var data = await ReadLimitedAsync(response.Content, Math.Max(0, request.MaxBytes - getBytes()), addBytes, token);
        try { return RobotsRules.Parse(new System.Text.UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF'), token); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or TimeoutException) { throw new RobotsException(); }
    }

    private static async Task<(string Hash, string Path, long Bytes)> SaveBoundedAsync(HttpResponseMessage response, CollectionRequest req, Func<long> getBytes, Action<long> addBytes, CancellationToken token)
    {
        var declared = response.Content.Headers.ContentLength;
        if (declared is > 0 && declared > req.MaxBytes - getBytes()) throw new BudgetException("oversized");
        var dir = Path.GetFullPath(req.ArtifactDirectory);
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, ".capture-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var input = await response.Content.ReadAsStreamAsync(token);
            using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var output = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920]; long size = 0;
            while (true)
            {
                var allowed = Math.Max(0, req.MaxBytes - getBytes());
                var readLimit = (int)Math.Min(buffer.Length, Math.Max(1, allowed + 1));
                var read = await input.ReadAsync(buffer.AsMemory(0, readLimit), token);
                if (read == 0) break;
                var counted = Math.Min(read, allowed); addBytes(counted);
                if (counted != read) throw new BudgetException("oversized");
                size += read;
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (declared is not null && size != declared.Value) throw new IOException("Response length did not match Content-Length.");
            await output.FlushAsync(token);
            output.Dispose();
            await file.FlushAsync(token);
            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var destinationName = digest + ".gz";
            var destination = Path.Combine(dir, destinationName);
            var existing = new FileInfo(destination);
            if (existing.LinkTarget is not null) throw new IOException("Existing artifact cannot be a symbolic link.");
            if (File.Exists(destination))
            {
                await VerifyExistingAsync(destination, digest, size, token);
                File.Delete(temp);
            }
            else File.Move(temp, destination);
            return (digest, destinationName, size);
        }
        catch (HttpRequestException exception) { throw new IOException("Response body transport failed before completion.", exception); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task VerifyExistingAsync(string path, string digest, long expectedBytes, CancellationToken token)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Existing artifact cannot be a symbolic link.");
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[8192];
        long bytes = 0;
        int read;
        while ((read = await gzip.ReadAsync(buffer, token)) != 0)
        {
            bytes += read;
            if (bytes > expectedBytes) throw new IOException("Existing artifact exceeds expected size.");
            hash.AppendData(buffer, 0, read);
        }
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (bytes != expectedBytes || actual != digest) throw new IOException("Existing artifact failed integrity verification.");
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, long max, Action<long> addBytes, CancellationToken token)
    {
        var declared = content.Headers.ContentLength;
        if (declared is > 0 && declared.Value > max) throw new BudgetException("oversized");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var remaining = max - memory.Length;
            var requestSize = (int)Math.Min(buffer.Length, Math.Max(1, remaining + 1));
            var read = await stream.ReadAsync(buffer.AsMemory(0, requestSize), token);
            if (read == 0) break;
            var counted = (int)Math.Min(read, Math.Max(0, remaining));
            addBytes(counted);
            if (counted != read) throw new BudgetException("oversized");
            await memory.WriteAsync(buffer.AsMemory(0, read), token);
        }
        if (declared is not null && memory.Length != declared.Value) throw new IOException("Response length did not match Content-Length.");
        return memory.ToArray();
    }
    private static void EnsureBudget(CollectionRequest request, int count)
    {
        if (count >= request.MaxRequests) throw new BudgetException("requestBudget");
    }

    private static long? RetrySeconds(RetryConditionHeaderValue? value)
    {
        var delay = value?.Delta ?? (value?.Date - DateTimeOffset.UtcNow);
        return delay is { } duration ? Math.Max(0, (long)Math.Ceiling(duration.TotalSeconds)) : null;
    }

    private static async Task Delay(int milliseconds, CancellationToken token)
    {
        if (milliseconds > 0) await Task.Delay(milliseconds, token);
    }
    public void Dispose() => client.Dispose();
    private sealed class BudgetException(string code) : Exception { public string Code { get; } = code; }
    private sealed class RobotsException : Exception { }

    /// <summary>Supports wildcard user-agent groups and Disallow path rules with * and terminal $. Unknown directives are ignored.</summary>
    private sealed class RobotsRules
    {
        private readonly List<string> disallowed = [];
        public bool Allowed(string path, CancellationToken token)
        {
            try
            {
                var normalized = NormalizeUnreserved(path);
                foreach (var rule in disallowed)
                {
                    token.ThrowIfCancellationRequested();
                    if (RuleMatches(NormalizeUnreserved(rule), normalized)) return false;
                }
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or TimeoutException)
            {
                throw new RobotsException();
            }
        }
        private static string NormalizeUnreserved(string value)
        {
            value = Regex.Replace(value, "[^\\x00-\\x7F]+", match => Uri.EscapeDataString(match.Value));
            var output = new System.Text.StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '%' && i + 2 < value.Length && byte.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var decoded))
                {
                    var c = (char)decoded;
                    if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~') { output.Append(c); i += 2; continue; }
                    output.Append('%').Append(char.ToUpperInvariant(value[i + 1])).Append(char.ToUpperInvariant(value[i + 2])); i += 2; continue;
                }
                output.Append(value[i]);
            }
            return output.ToString();
        }
        public static RobotsRules Parse(string text, CancellationToken token)
        {
            var result = new RobotsRules();
            var agents = new List<string>();
            var rules = new List<string>();
            void Flush()
            {
                if (agents.Any(a => a == "*" || a.Contains("civiclens", StringComparison.OrdinalIgnoreCase))) result.disallowed.AddRange(rules);
                agents.Clear(); rules.Clear();
            }
            foreach (var rawLine in text.Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                var clean = rawLine.Split('#')[0].Trim();
                if (clean.Length == 0) { if (rules.Count > 0) Flush(); continue; }
                var parts = clean.Split(':', 2);
                if (parts.Length != 2) continue;
                var key = parts[0].Trim(); var value = parts[1].Trim();
                if (key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                {
                    if (rules.Count > 0) Flush();
                    agents.Add(value.ToLowerInvariant());
                }
                else if (key.Equals("disallow", StringComparison.OrdinalIgnoreCase) && value.Length != 0)
                {
                    if (value.Length > 2048) throw new RobotsException();
                    rules.Add(value);
                }
            }
            Flush();
            return result;
        }
        private static bool RuleMatches(string rule, string path)
        {
            var end = rule.EndsWith('$'); if (end) rule = rule[..^1];
            var pattern = "^" + Regex.Escape(rule).Replace("\\*", ".*") + (end ? "$" : ".*");
            return Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
        }
    }
}
