using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;
using CivicLens.Collection.Contracts;

namespace CivicLens.Collector.Http;

public sealed class HttpCollector : IDisposable
{
    private const int MaximumRobotsDecodedStageBytes = 10_000_000;
    private const int MaximumRobotsContentEncodings = 16;
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
        request.Validate();
        var requests = 0;
        var robotsRequests = 0;
        long? robotsCrawlDelayMilliseconds = null;
        long bytes = 0;
        var current = new Uri(request.Url);
        var requestedUri = current;
        HttpResponseMetadata? responseMetadata = null;
        HttpRequestValidators? sentValidators = null;
        var elapsed = Stopwatch.StartNew();
        var lastRequestAt = TimeSpan.Zero;
        var effectiveDelayMilliseconds = (long)request.MinDelayMilliseconds;
        var token = cancellationToken;
        var rules = RobotsRules.Empty;
        var robotsPhase = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
            token = timeout.Token;
            try
            {
                rules = await GetRobotsAsync(request, () =>
                {
                    EnsureBudget(request, requests);
                    requests++;
                    robotsRequests++;
                    lastRequestAt = elapsed.Elapsed;
                }, n => bytes += n, () => bytes, token, DelayBeforeRobotsRedirectAsync);
                robotsCrawlDelayMilliseconds = request.Version == CollectionProtocol.Version
                    ? rules.CrawlDelayMilliseconds
                    : null;
                robotsPhase = false;
            }
            catch (RobotsException exception)
            {
                if (request.Version != CollectionProtocol.Version)
                    return Result(CollectionOutcome.Failed, CollectionFailureCode.RobotsUnavailable);
                return Result(exception.Deferred ? CollectionOutcome.Deferred : CollectionOutcome.Failed,
                    exception.FailureCode, retry: exception.RetryAfterSeconds);
            }
            catch (HttpRequestException)
            {
                return request.Version == CollectionProtocol.Version
                    ? Result(CollectionOutcome.Deferred, CollectionFailureCode.RobotsUnavailable)
                    : Result(CollectionOutcome.Failed, CollectionFailureCode.RobotsUnavailable);
            }
            if (!RobotsAllows(current.PathAndQuery)) return Result(CollectionOutcome.Failed, CollectionFailureCode.RobotsDenied);
            effectiveDelayMilliseconds = Math.Max((long)request.MinDelayMilliseconds, robotsCrawlDelayMilliseconds ?? 0);
            if (await DeferForDelayIfNeededAsync())
                return Result(CollectionOutcome.Deferred, CollectionFailureCode.CrawlDelay, retry: RetryDelaySeconds(effectiveDelayMilliseconds));
            var target = current;
            while (true)
            {
                EnsureBudget(request, requests);
                if (!request.Allows(target)) return Result(CollectionOutcome.Failed, CollectionFailureCode.OutOfScope);
                using var message = new HttpRequestMessage(HttpMethod.Get, target);
                message.Headers.UserAgent.ParseAdd("CivicLens/0.1");
                if (target == requestedUri)
                {
                    if (request.ETag is not null) message.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(request.ETag));
                    if (request.LastModified is not null) message.Headers.IfModifiedSince = request.LastModified;
                }
                var hasConditionals = message.Headers.IfNoneMatch.Count > 0 || message.Headers.IfModifiedSince is not null;
                current = target;
                responseMetadata = null;
                sentValidators = ReadRequestValidators(message);
                requests++;
                lastRequestAt = elapsed.Elapsed;
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token);
                try { responseMetadata = ReadResponseMetadata(response); }
                catch (InvalidDataException) { return Result(CollectionOutcome.Failed, CollectionFailureCode.InvalidResponse); }
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    if (!hasConditionals) return Result(CollectionOutcome.Failed, CollectionFailureCode.UnexpectedNotModified);
                    return Result(CollectionOutcome.NotModified);
                }
                if ((int)response.StatusCode == 429)
                    return Result(CollectionOutcome.Deferred, CollectionFailureCode.RateLimited, retry: RetrySeconds(response.Headers.RetryAfter));
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (response.Headers.Location is null) return Result(CollectionOutcome.Failed, CollectionFailureCode.RedirectMissingLocation);
                    var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(target, response.Headers.Location);
                    if (!request.Allows(next)) return Result(CollectionOutcome.Failed, CollectionFailureCode.OutOfScope);
                    if (!RobotsAllows(next.PathAndQuery)) return Result(CollectionOutcome.Failed, CollectionFailureCode.RobotsDenied);
                    if (await DeferForDelayIfNeededAsync())
                        return Result(CollectionOutcome.Deferred, CollectionFailureCode.CrawlDelay, retry: RetryDelaySeconds(effectiveDelayMilliseconds));
                    target = next;
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK) return Result(CollectionOutcome.Failed, CollectionFailureCode.HttpError);
                var artifact = await SaveBoundedAsync(response, request, () => bytes, n => bytes += n, token);
                var discovery = request.Mode switch
                {
                    CollectionMode.Feed => await FeedParser.ParseCaptureAsync(Path.Combine(request.ArtifactDirectory, artifact.RelativePath), request,
                        current.AbsoluteUri, responseMetadata!.ContentEncodings, responseMetadata.ContentType, token),
                    CollectionMode.Html => await HtmlParser.ParseCaptureAsync(Path.Combine(request.ArtifactDirectory, artifact.RelativePath), request,
                        current.AbsoluteUri, responseMetadata!.ContentEncodings, responseMetadata.ContentType, token),
                    _ => null
                };
                return Result(CollectionOutcome.Captured, capture: artifact, discovery: discovery);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Result(CollectionOutcome.Failed, CollectionFailureCode.Cancelled); }
        catch (OperationCanceledException)
        {
            return robotsPhase && request.Version == CollectionProtocol.Version
                ? Result(CollectionOutcome.Deferred, CollectionFailureCode.RobotsUnavailable)
                : Result(CollectionOutcome.Failed, CollectionFailureCode.Timeout);
        }
        catch (BudgetException e) { return Result(CollectionOutcome.Failed, e.Code); }
        catch (RobotsException) { return Result(CollectionOutcome.Failed, CollectionFailureCode.RobotsUnavailable); }
        catch (HttpRequestException) { return Result(CollectionOutcome.Failed, CollectionFailureCode.TransportError); }
        catch (UnauthorizedAccessException) { return Result(CollectionOutcome.Failed, CollectionFailureCode.ArtifactWriteFailed); }
        catch (IOException) { return Result(CollectionOutcome.Failed, CollectionFailureCode.IncompleteResponse); }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Result(CollectionOutcome.Failed, CollectionFailureCode.InvalidResponse);
        }

        CollectionResult Result(CollectionOutcome outcome, CollectionFailureCode? failure = null,
            CaptureArtifact? capture = null, long? retry = null, DiscoveryResult? discovery = null)
        {
            var result = new CollectionResult
            {
                Version = request.Version,
                JobId = request.JobId,
                SourceId = request.SourceId,
                RequestedUrl = request.Url,
                FinalUrl = current.AbsoluteUri,
                Outcome = outcome,
                ObservedAt = DateTimeOffset.UtcNow,
                Response = responseMetadata,
                SentValidators = sentValidators,
                Capture = capture,
                Discovery = discovery,
                BytesReceived = bytes,
                RequestCount = requests,
                RobotsRequestCount = request.Version == CollectionProtocol.Version ? robotsRequests : null,
                RobotsCrawlDelayMilliseconds = request.Version == CollectionProtocol.Version ? robotsCrawlDelayMilliseconds : null,
                FailureCode = failure,
                RetryAfterSeconds = retry
            };
            result.ValidateAgainst(request);
            return result;
        }

        async Task<bool> DeferForDelayIfNeededAsync()
        {
            var waitMilliseconds = effectiveDelayMilliseconds - (long)(elapsed.Elapsed - lastRequestAt).TotalMilliseconds;
            if (waitMilliseconds <= 0) return false;
            if (request.Version != CollectionProtocol.Version)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(waitMilliseconds), token);
                return false;
            }
            var remainingMilliseconds = request.TimeoutSeconds * 1000L - (long)elapsed.Elapsed.TotalMilliseconds;
            if (waitMilliseconds >= remainingMilliseconds) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(waitMilliseconds), token);
            return false;
        }

        Task<bool> DelayBeforeRobotsRedirectAsync() => DeferForDelayIfNeededAsync();

        bool RobotsAllows(string path)
        {
            try { return rules.Allowed(path, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or TimeoutException)
            { throw new RobotsException(CollectionFailureCode.RobotsUnavailable); }
        }
    }

    private static HttpRequestValidators? ReadRequestValidators(HttpRequestMessage message)
    {
        var etag = message.Headers.IfNoneMatch.Count == 0 ? null :
            message.Headers.GetValues("If-None-Match").Single();
        var modified = message.Headers.IfModifiedSince is null ? (DateTimeOffset?)null :
            DateTimeOffset.ParseExact(message.Headers.GetValues("If-Modified-Since").Single(),
                "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        return etag is null && modified is null ? null :
            new HttpRequestValidators { ETag = etag, LastModified = modified };
    }

    private static HttpResponseMetadata ReadResponseMetadata(HttpResponseMessage response)
    {
        var metadata = new HttpResponseMetadata
        {
            StatusCode = (int)response.StatusCode,
            ETag = SingleHeader(response.Headers, "ETag"),
            LastModified = ReadLastModified(response.Content.Headers),
            ContentType = SingleHeader(response.Content.Headers, "Content-Type"),
            ContentEncodings = response.Content.Headers.NonValidated.TryGetValues("Content-Encoding", out var values)
                ? values.SelectMany(value => value.Split(',')).Select(value => value.Trim()).ToArray()
                : []
        };
        metadata.Validate();
        return metadata;
    }

    private static DateTimeOffset? ReadLastModified(HttpContentHeaders headers)
    {
        if (SingleHeader(headers, "Last-Modified") is null) return null;
        return headers.LastModified ?? throw new InvalidDataException("Last-Modified must be a valid HTTP date.");
    }

    private static string? SingleHeader(HttpHeaders headers, string name)
    {
        if (!headers.NonValidated.TryGetValues(name, out var values)) return null;
        var fields = values.ToArray();
        if (fields.Length != 1) throw new InvalidDataException($"Expected one {name} field.");
        return fields[0];
    }

    private async Task<RobotsRules> GetRobotsAsync(CollectionRequest request, Action count, Action<long> addBytes,
        Func<long> getBytes, CancellationToken token, Func<Task<bool>> delayBeforeRedirectAsync)
    {
        var origin = new Uri(request.AllowedOrigin);
        var target = new Uri(origin, "/robots.txt");
        while (true)
        {
            count();
            using var robotRequest = new HttpRequestMessage(HttpMethod.Get, target);
            robotRequest.Headers.UserAgent.ParseAdd("CivicLens/0.1");
            robotRequest.Headers.AcceptEncoding.ParseAdd("gzip, deflate, br");
            using var response = await client.SendAsync(robotRequest, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.NotFound) return RobotsRules.Empty;
            if ((int)response.StatusCode == 429)
                throw new RobotsException(CollectionFailureCode.RateLimited, deferred: true,
                    retryAfterSeconds: RetrySeconds(response.Headers.RetryAfter));
            if ((int)response.StatusCode is >= 500 and < 600)
                throw new RobotsException(CollectionFailureCode.RobotsUnavailable, deferred: true,
                    retryAfterSeconds: RetrySeconds(response.Headers.RetryAfter));
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (request.Version != CollectionProtocol.Version)
                    throw new RobotsException(CollectionFailureCode.RobotsUnavailable);
                if (response.Headers.Location is null) throw new RobotsException(CollectionFailureCode.RobotsUnavailable);
                Uri next;
                try { next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(target, response.Headers.Location); }
                catch (UriFormatException) { throw new RobotsException(CollectionFailureCode.RobotsUnavailable); }
                if (!SameOrigin(origin, next) || next.Fragment.Length != 0 || next.UserInfo.Length != 0)
                    throw new RobotsException(CollectionFailureCode.RobotsUnavailable);
                if (await delayBeforeRedirectAsync())
                    throw new RobotsException(CollectionFailureCode.CrawlDelay, deferred: true,
                        retryAfterSeconds: RetryDelaySeconds(request.MinDelayMilliseconds));
                target = next;
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new RobotsException(CollectionFailureCode.RobotsUnavailable);

            try
            {
                var encoded = await ReadLimitedAsync(response.Content, Math.Max(0, request.MaxBytes - getBytes()), addBytes, token);
                var encodings = response.Content.Headers.ContentEncoding
                    .SelectMany(value => value.Split(','))
                    .Select(value => value.Trim())
                    .ToArray();
                if (encodings.Length > MaximumRobotsContentEncodings || encodings.Any(string.IsNullOrEmpty))
                    throw new InvalidDataException("Robots response has too many or malformed content encodings.");
                var data = await DecodeRobotsBodyAsync(encoded, encodings, token);
                return RobotsRules.Parse(new System.Text.UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF'), token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is ArgumentException or FormatException or NotSupportedException or TimeoutException or IOException or InvalidDataException)
            { throw new RobotsException(CollectionFailureCode.RobotsUnavailable); }
        }
    }

    private static async Task<byte[]> DecodeRobotsBodyAsync(byte[] encoded, IReadOnlyList<string> contentEncodings,
        CancellationToken token)
    {
        Stream decoded = new MemoryStream(encoded, writable: false);
        var owned = new List<Stream> { decoded };
        try
        {
            foreach (var coding in contentEncodings.Reverse())
            {
                token.ThrowIfCancellationRequested();
                var decoder = coding.ToLowerInvariant() switch
                {
                    "identity" => decoded,
                    "gzip" or "x-gzip" => new GZipStream(decoded, CompressionMode.Decompress, leaveOpen: true),
                    "deflate" => new ZLibStream(decoded, CompressionMode.Decompress, leaveOpen: true),
                    "br" => new BrotliStream(decoded, CompressionMode.Decompress, leaveOpen: true),
                    _ => throw new InvalidDataException("Robots response uses an unsupported content encoding.")
                };
                if (!ReferenceEquals(decoder, decoded))
                {
                    owned.Add(decoder);
                    decoded = decoder;
                }
                var boundedStage = new BoundedRobotsReadStream(decoded, MaximumRobotsDecodedStageBytes);
                owned.Add(boundedStage);
                decoded = boundedStage;
            }
            var finalBounded = new BoundedRobotsReadStream(decoded, MaximumRobotsDecodedStageBytes);
            owned.Add(finalBounded);
            await using var output = new MemoryStream();
            await finalBounded.CopyToAsync(output, 81920, token);
            // Drain every stage so a downstream decoder cannot hide a large or
            // corrupt intermediate representation from an outer content layer.
            foreach (var stage in owned.OfType<BoundedRobotsReadStream>().Reverse())
                await stage.CopyToAsync(Stream.Null, 81920, token);
            return output.ToArray();
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--) await owned[i].DisposeAsync();
        }
    }

    private static bool SameOrigin(Uri origin, Uri target) => target.IsAbsoluteUri &&
        target.Scheme == origin.Scheme && target.IdnHost == origin.IdnHost && target.Port == origin.Port;

    private static async Task<CaptureArtifact> SaveBoundedAsync(HttpResponseMessage response, CollectionRequest req, Func<long> getBytes, Action<long> addBytes, CancellationToken token)
    {
        var declared = response.Content.Headers.ContentLength;
        if (declared is > 0 && declared > req.MaxBytes - getBytes()) throw new BudgetException(CollectionFailureCode.Oversized);
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
                if (counted != read) throw new BudgetException(CollectionFailureCode.Oversized);
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
            return new CaptureArtifact { Sha256 = digest, RelativePath = destinationName, ByteLength = size };
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
        if (declared is > 0 && declared.Value > max) throw new BudgetException(CollectionFailureCode.Oversized);
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
            if (counted != read) throw new BudgetException(CollectionFailureCode.Oversized);
            await memory.WriteAsync(buffer.AsMemory(0, read), token);
        }
        if (declared is not null && memory.Length != declared.Value) throw new IOException("Response length did not match Content-Length.");
        return memory.ToArray();
    }
    private static void EnsureBudget(CollectionRequest request, int count)
    {
        if (count >= request.MaxRequests) throw new BudgetException(CollectionFailureCode.RequestBudget);
    }

    private static long? RetrySeconds(RetryConditionHeaderValue? value)
    {
        var delay = value?.Delta ?? (value?.Date - DateTimeOffset.UtcNow);
        return delay is { } duration ? Math.Max(0, (long)Math.Ceiling(duration.TotalSeconds)) : null;
    }

    private static long RetryDelaySeconds(long delayMilliseconds) =>
        delayMilliseconds / 1000 + (delayMilliseconds % 1000 == 0 ? 0 : 1);

    private sealed class BoundedRobotsReadStream(Stream inner, long maximum) : Stream
    {
        private long bytesRead;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Check(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Check(inner.Read(buffer));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsyncCore(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ReadAsyncCore(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) => base.Dispose(disposing);

        private async ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancellationToken cancellationToken) =>
            Check(await inner.ReadAsync(buffer, cancellationToken));

        private int Check(int count)
        {
            bytesRead += count;
            if (bytesRead > maximum) throw new InvalidDataException("Robots content decoding exceeded its stage limit.");
            return count;
        }
    }

    public void Dispose() => client.Dispose();
    private sealed class BudgetException(CollectionFailureCode code) : Exception { public CollectionFailureCode Code { get; } = code; }
    private sealed class RobotsException(CollectionFailureCode failureCode, bool deferred = false,
        long? retryAfterSeconds = null) : Exception
    {
        public CollectionFailureCode FailureCode { get; } = failureCode;
        public bool Deferred { get; } = deferred;
        public long? RetryAfterSeconds { get; } = retryAfterSeconds;
    }

}
