# Polly vs HttpClient

A proof-of-concept to test the behaviour of Polly's [timeout strategy](https://www.pollydocs.org/strategies/timeout.html)
when combined with `HttpClient`'s own [Timeout property](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-7.0)

The projects consists in:
- an API that exposes two routes:
    - a fast route that responds ~500ms
    - a slow route that responds in ~5s
- a background services that calls either route using a resilience strategy configured with Polly

## Conclusion

Polly's own timeout strategy has a more flexible timeout configuration (i.e. on a per-request basis)

If configured properly, it can conveniently replace `HttpClient.Timeout`:

```csharp
var timeoutForEachSingleRetry = TimeSpan.FromSeconds(1);

new ResiliencePipelineBuilder()
    .AddRetry(new()
    {
        MaxRetryAttempts = 5,
        BackoffType = DelayBackoffType.Constant,
    })
    .AddTimeout(timeoutForEachSingleRetry)
    .Build();
```

But with some caveats:

- Timeout has to be *above 1 second*.
- if a custom predicate is configured, it has to *explicitly* handle `Polly.Timeout.TimeoutRejectedException`:

```csharp 
new ResiliencePipelineBuilder()
    .AddRetry(new()
    {
        ShouldHandle = somePredicateBuilder.Handle<Polly.Timeout.TimeoutRejectedException>()
    })
    .AddTimeout(TimeSpan.FromSeconds(1))
    .Build();
```
