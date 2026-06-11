namespace JobTracker.Core;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that retries transient failures with
/// exponential back-off. Handles network errors and HTTP 5xx responses.
///
/// Delays: 2 s → 4 s → 8 s (three retries, then the exception is rethrown).
///
/// Note: safe for GET-only usage (all fetchers). Do not use for POST/PUT
/// requests with a streaming body — the content stream cannot be replayed.
/// </summary>
public class HttpRetryHandler : DelegatingHandler
{
    private const int    MaxRetries       = 3;
    private const double BaseDelaySeconds = 2.0;

    public HttpRetryHandler() : base(new HttpClientHandler()) { }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> backed by this handler.
    /// Call once per fetch run; dispose after use.
    /// </summary>
    public static HttpClient CreateClient() =>
        new(new HttpRetryHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken  ct)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                response = await base.SendAsync(request, ct);

                // Success or client error (4xx) — do not retry.
                if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                    return response;

                // HTTP 5xx — server-side transient error, worth retrying.
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                // Network-level failure — retry.
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested
                                                 && attempt < MaxRetries)
            {
                // Request timed out (not user-cancelled) — retry.
            }

            if (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(BaseDelaySeconds * Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
        }

        // Final attempt — let the caller handle whatever comes back.
        return await base.SendAsync(request, ct);
    }
}
