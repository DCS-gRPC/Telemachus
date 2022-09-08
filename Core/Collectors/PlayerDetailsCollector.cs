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
using RurouniJones.Dcs.Grpc.V0.Common;
using RurouniJones.Dcs.Grpc.V0.Net;

namespace RurouniJones.Telemachus.Core.Collectors
{ 
    public class PlayerDetailsCollector : ICollector
    {
        private readonly ILogger<PlayerDetailsCollector> _logger;

        private readonly Meter _meter;
        private readonly Histogram<int> _playerPings; // In milliseconds

        public PlayerDetailsCollector(ILogger<PlayerDetailsCollector> logger)
        {
            _logger = logger;

            _meter = new Meter("Telemachus.Core.Collectors.PlayerCountCollector");
            _playerPings = _meter.CreateHistogram<int>("player_pings", "milliseconds", "Player Ping Times in milliseconds");
        }

        public async Task MonitorAsync(ICollector.CollectorConfig config)
        {
            var serverShortName = config.ServerShortName;
            var channel = config.Channel;
            var stoppingToken = config.SessionStoppingToken;
            var sessionId = config.SessionId;

            _logger.LogDebug("Executing PlayerDetailsCollector");
            _meter.CreateObservableGauge("players", () => { return GetPlayersOnServer(serverShortName, sessionId, channel, stoppingToken).Result; },
                description: "The number of players on a server");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            _meter.Dispose();
        }

        private async Task<List<Measurement<int>>> GetPlayersOnServer(string serverShortName, long sessionId, GrpcChannel channel, CancellationToken stoppingToken)
        {
            _logger.LogTrace("Getting Players for {shortName}", serverShortName);
            List<Measurement<int>> results = new();

            var sessionTag = new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, sessionId);
            var serverTag = new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName);

            var service = new NetService.NetServiceClient(channel);
            try
            {
                var response = await service.GetPlayersAsync(new GetPlayersRequest {}, deadline: DateTime.UtcNow.AddSeconds(0.5), cancellationToken: stoppingToken);
                var players = response.Players;

                // Get Coalition counts
                var blueFor = players.Count(x => x.Coalition == Coalition.Blue);
                var redFor = players.Count(x => x.Coalition == Coalition.Red);

                results.Add(new Measurement<int>(blueFor,
                    sessionTag,
                    serverTag,
                    new KeyValuePair<string, object?>("coalition", "Blue")));
                results.Add(new Measurement<int>(redFor,
                    sessionTag,
                    serverTag,
                    new KeyValuePair<string, object?>("coalition", "Red")));

                // While we are here. Get the pings for the players and add them to the ping Histogram.
                foreach(var player in players)
                {
                    if(player.Id == 1) continue; // Server player. Ignore
                    _playerPings.Record((int)player.Ping, sessionTag, serverTag);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded || ex.StatusCode == StatusCode.Cancelled)
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
