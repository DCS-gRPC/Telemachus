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
        private readonly string _serverShortName;
        private readonly long _sessionId;
        private readonly GrpcChannel _channel;
        private readonly CancellationToken _stoppingToken;
        private readonly Meter _meter;


        public BallisticCollector(ILogger<BallisticCollector> logger, ICollector.CollectorConfig config)
        {
            _logger = logger;
            _serverShortName = config.ServerShortName;
            _channel = config.Channel;
            _stoppingToken = config.SessionStoppingToken;
            _sessionId = config.SessionId;
            _meter = new Meter("Telemachus.Core.Collectors.BallisticCollector");
        }

        public async Task MonitorAsync()
        {
            _logger.LogDebug("Executing BallisticsCollector");
            _meter.CreateObservableGauge("ballistics", () => { return GetBallisticsOnServer().Result; },
                description: "The number of ballistic objects on a server");
            await Task.Delay(Timeout.Infinite, _stoppingToken);
            _meter.Dispose();
        }

        private async Task<List<Measurement<int>>> GetBallisticsOnServer()
        {
            _logger.LogTrace("Getting Ballistics for {shortName}", _serverShortName);
            List<Measurement<int>> results = new();

            var sessionTag = new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _sessionId);
            var serverTag = new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, _serverShortName);

            var service = new HookService.HookServiceClient(_channel);
            try
            {
                var response = await service.GetBallisticsCountAsync(new GetBallisticsCountRequest { }, deadline: DateTime.UtcNow.AddSeconds(0.5), cancellationToken: _stoppingToken);
                var ballisticsCount = (int)response.Count;

                results.Add(new Measurement<int>(ballisticsCount, sessionTag, serverTag));
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("Timed out calling {shortName}", _serverShortName);
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception calling {shortName}. Exception {exception}", _serverShortName, ex.Message);
            }

            return results;
        }
    }
}
