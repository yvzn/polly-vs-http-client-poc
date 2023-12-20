using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Polly;
using Polly.Retry;

namespace PollyVsHttpClient;

internal class PollyVsHttpClientHostedService : BackgroundService
{
    private readonly IServer server;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<PollyVsHttpClientHostedService> logger;

    public PollyVsHttpClientHostedService(IServer server, IHttpClientFactory httpClientFactory, ILogger<PollyVsHttpClientHostedService> logger)
    {
        this.server = server;
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(5_000, stoppingToken);

        // https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling
        try
        {
            await MakeSomeRequestsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MakeSomeRequestsAsync crashed");
        }
    }

    private async Task MakeSomeRequestsAsync(CancellationToken stoppingToken)
    {
        var httpClientMode = "standard";
        var endpoint = "slow";
        var pollyTimeout = TimeSpan.FromSeconds(1);

        var resilienceStrategy = BuildResilienceStrategy(pollyTimeout);
        HttpClient httpClient = BuildHttpClient(httpClientMode);

        var response = await resilienceStrategy.ExecuteAsync(async (ct) => await httpClient.GetAsync(endpoint, ct), stoppingToken);

        logger.LogInformation("🧪 Response: {StatusCode}", response.StatusCode);
        logger.LogDebug("🧪 Content: {StatusCode}", await response.Content.ReadAsStringAsync(stoppingToken));
    }

    private HttpClient BuildHttpClient(string httpClientName)
    {
        // httpClient.Timeout can be superseded by Polly's own timeout, with a more flexible configuration: it can basically be ignored

        var httpClient = httpClientFactory.CreateClient(httpClientName);
        var baseAddress = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? "http://localhost";

        logger.LogInformation("🧪 BaseAddress: {BaseAddress}", baseAddress);
        httpClient.BaseAddress = new Uri(baseAddress);

        return httpClient;
    }

    private ResiliencePipeline BuildResilienceStrategy(TimeSpan timeoutForEachSingleRetry)
    {
        PredicateBuilder<object>? retryPredicate = BuildRetryPredicate();

        // resilience strategy can be configured on the httpclient in .NET 8
        // https://devblogs.microsoft.com/dotnet/building-resilient-cloud-services-with-dotnet-8/

        return new ResiliencePipelineBuilder()
                .AddRetry(new()
                {
                    ShouldHandle = retryPredicate,
                    Delay = TimeSpan.FromSeconds(1),
                    MaxRetryAttempts = 5,
                    BackoffType = DelayBackoffType.Constant,
                    OnRetry = (OnRetryArguments<object> args) =>
                    {
                        logger.LogDebug("🧪 Retrying: {Attempt}", args.AttemptNumber);
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(timeoutForEachSingleRetry)
                .Build();
    }

    private static PredicateBuilder<object> BuildRetryPredicate()
    {
        // when using a custom predicate for retry strategies
        // it has to explicitly handle Polly.Timeout.TimeoutRejectedException
        // otherwise polly will not retry on timeout

        static bool IsHttpException(Exception ex) => ex is SocketException or IOException or HttpRequestException;
        static bool IsPollyTimeoutException(Exception ex) => ex is Polly.Timeout.TimeoutRejectedException;

        return new PredicateBuilder().Handle<Exception>(ex => IsHttpException(ex) || IsPollyTimeoutException(ex));
    }
}
