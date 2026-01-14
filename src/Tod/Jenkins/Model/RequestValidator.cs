using Serilog;

namespace Tod.Jenkins;

internal sealed class RequestValidator(JenkinsConfig config, IJenkinsClient jenkinsClient)
{
    public async Task<bool> Validate(RequestChain[] chains)
    {
        var queueSize = await jenkinsClient.GetQueueSize().ConfigureAwait(false);
        var totalDuration = chains.TotalDuration();
        if (config.LoadThresholds.Any(x => queueSize >= x.QueueSize && totalDuration >= x.MaxRequestDuration))
        {
            Log.Warning("The Jenkins queue size is {QueueSize} and the total request duration is {TotalDuration}. " +
                "This exceeds the configured thresholds. Aborting request registration to avoid overloading Jenkins. " +
                "Please retry later or with fewer jobs.",
                queueSize, totalDuration);
            return false;
        }
        return true;
    }
}
