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

using System.Collections.Concurrent;
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
        private readonly Session _session;
        private readonly Meter _meter;


        public BallisticCollector(ILogger<BallisticCollector> logger, Session session)
        {
            _logger = logger;
            _session = session;
            _meter = new Meter("Telemachus.Core.Collectors.BallisticCollector");
        }

        public void Execute(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            _logger.LogDebug("Executing BallisticsCollector");
            _meter.CreateObservableGauge("ballistics", () => { return GetBallisticsOnServers(gameServerChannels, stoppingToken); },
                description: "The number of ballistic objects on a server");
        }

        private List<Measurement<int>> GetBallisticsOnServers(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            ConcurrentBag<Measurement<int>> results = new();
            List<Task> tasks = new();

            foreach (KeyValuePair<string, GrpcChannel> server in gameServerChannels)
            {
                tasks.Add(Task.Run(async () =>
                {
                    foreach (var item in await GetBallisticsOnServer(server.Key, server.Value, stoppingToken))
                    {
                        results.Add(item);
                    }
                }, stoppingToken));
            }
            Task.WaitAll(tasks.ToArray(), stoppingToken);

            return results.ToList();
        }
        private async Task<List<Measurement<int>>> GetBallisticsOnServer(string shortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            _logger.LogDebug("Getting Ballistics for {shortName}", shortName);           
            List<Measurement<int>> results = new();

            var sessionTag = new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _session.GetSessionId(shortName));
            var serverTag = new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, shortName);

            var service = new HookService.HookServiceClient(channel);
            try
            {
                var response = await service.GetBallisticsCountAsync(new GetBallisticsCountRequest {}, deadline: DateTime.UtcNow.AddSeconds(0.5), cancellationToken: stoppingToken);
                var ballisticsCount = (int) response.Count;

                results.Add(new Measurement<int>(ballisticsCount, sessionTag, serverTag));
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("Timed out calling {shortName}", shortName);
            }
            catch (Exception ex)
            {
                _logger.LogError("Exception calling {shortName}. Exception {exception}", shortName, ex.Message);
            }

            return results;
        }
    }
}
