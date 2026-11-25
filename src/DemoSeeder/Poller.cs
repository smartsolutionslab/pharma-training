namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Waits for what the services learn of each other through events: looks again after 1, 2, 4, then every 5 seconds of
/// the <see cref="TimeProvider"/>, and gives up after the limit, naming what it waited for and the last answer.
/// </summary>
internal sealed class Poller(TimeProvider time, TimeSpan timeout)
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan LongestDelay = TimeSpan.FromSeconds(5);

    public async Task<T> UntilAsync<T>(string what, Func<CancellationToken, Task<Polled<T>>> look, CancellationToken cancellationToken)
        where T : class
    {
        var deadline = time.GetUtcNow() + timeout;
        var delay = FirstDelay;
        while (true)
        {
            var polled = await look(cancellationToken);
            if (polled.Value is not null)
            {
                return polled.Value;
            }

            if (time.GetUtcNow() >= deadline)
            {
                throw new SeedingException($"{what}: still {polled.NotYet} after {timeout}.");
            }

            await Task.Delay(delay, time, cancellationToken);
            delay = delay * 2 > LongestDelay ? LongestDelay : delay * 2;
        }
    }
}
