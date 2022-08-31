/* 
Custodian is a DCS server administration tool for Discord
Copyright (C) 2022 Jeffrey Jones

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Diagnostics.Metrics;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Hook;

namespace RurouniJones.Telemachus.Core.Collectors
{
    public class BallisticCollector : ICollector
    {
        private readonly ILogger<BallisticCollector> _logger;
        private readonly Meter _meter;


        public BallisticCollector(ILogger<BallisticCollector> logger)
        {
            _logger = logger;
            _meter = new Meter("Telemachus.Core.Collectors.BallisticCollector");
        }

        public async Task MonitorAsync(ICollector.CollectorConfig config)
        {
            var serverShortName = config.ServerShortName;
            var channel = config.Channel;
            var stoppingToken = config.SessionStoppingToken;
            var sessionId = config.SessionId;

            _logger.LogDebug("Executing BallisticsCollector");
            _meter.CreateObservableGauge("ballistics", () => { return GetBallisticsOnServer(serverShortName, sessionId, channel, stoppingToken).Result; },
                description: "The number of ballistic objects on a server");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            _meter.Dispose();
        }

        private async Task<List<Measurement<int>>> GetBallisticsOnServer(string serverShortName, long sessionId, GrpcChannel channel, CancellationToken stoppingToken)
        {
            _logger.LogTrace("Getting Ballistics for {shortName}", serverShortName);
            List<Measurement<int>> results = new();

            var sessionTag = new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, sessionId);
            var serverTag = new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName);

            var service = new HookService.HookServiceClient(channel);
            try
            {
                var response = await service.GetBallisticsCountAsync(new GetBallisticsCountRequest { }, deadline: DateTime.UtcNow.AddSeconds(0.5), cancellationToken: stoppingToken);
                var ballisticsCount = (int)response.Count;

                results.Add(new Measurement<int>(ballisticsCount, sessionTag, serverTag));
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("Timed out calling {shortName}", serverShortName);
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception calling {shortName}. Exception {exception}", serverShortName, ex.Message);
            }

            return results;
        }
    }
}
