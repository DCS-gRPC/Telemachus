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
        private readonly string _serverShortName;
        private readonly long _sessionId;
        private readonly GrpcChannel _channel;
        private readonly CancellationToken _stoppingToken;

        private readonly Meter _meter;
        private readonly Histogram<int> _playerPings; // In milliseconds

        public PlayerDetailsCollector(ILogger<PlayerDetailsCollector> logger, ICollector.CollectorConfig config)
        {
            _logger = logger;
            _serverShortName = config.ServerShortName;
            _channel = config.Channel;
            _stoppingToken = config.SessionStoppingToken;
            _sessionId = config.SessionId;

            _meter = new Meter("Telemachus.Core.Collectors.PlayerCountCollector");
            _playerPings = _meter.CreateHistogram<int>("player_pings", "milliseconds", "Player Ping Times in milliseconds");
        }

        public async Task MonitorAsync()
        {
            _logger.LogDebug("Executing PlayerDetailsCollector");
            _meter.CreateObservableGauge("players", () => { return GetPlayersOnServer().Result; },
                description: "The number of players on a server");
            await Task.Delay(Timeout.Infinite, _stoppingToken);
            _meter.Dispose();
        }

        private async Task<List<Measurement<int>>> GetPlayersOnServer()
        {
            _logger.LogDebug("Getting Players for {shortName}", _serverShortName);
            List<Measurement<int>> results = new();

            var sessionTag = new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _sessionId);
            var serverTag = new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, _serverShortName);

            var service = new NetService.NetServiceClient(_channel);
            try
            {
                var response = await service.GetPlayersAsync(new GetPlayersRequest {}, deadline: DateTime.UtcNow.AddSeconds(0.5), cancellationToken: _stoppingToken);
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
