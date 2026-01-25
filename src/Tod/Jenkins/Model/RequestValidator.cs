using Serilog;
using Tod.Core;

namespace Tod.Jenkins;

internal sealed class RequestValidator(JenkinsConfig config, IJenkinsClient jenkinsClient)
{
    public async Task<bool> Validate(RequestChain[] chains, int userActiveRequestsCount)
    {
        if (config.MaxUserActiveRequests.HasValue)
        {
            if (userActiveRequestsCount >= config.MaxUserActiveRequests.Value)
            {
                Log.Warning("User {User} already has {ActiveRequests} active request(s), which equals or exceeds the maximum of {MaxRequests}. " +
                    "Aborting request registration. Please wait for some requests to complete or abort them.",
                    Environment.UserName, userActiveRequestsCount, config.MaxUserActiveRequests.Value);
                return false;
            }
        }

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
