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
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Common;
using RurouniJones.Dcs.Grpc.V0.Mission;

namespace RurouniJones.Telemachus.Core.Collectors
{
    public class UnitCollector : ICollector
    {
        private record UnitSummary
        {
            public uint Id;
            public string Category;

            public UnitSummary(uint id, string category)
            {
                Id = id;
                Category = category;
            }
        }

        private readonly ILogger<EventCollector> _logger;

        private readonly Meter _meter;

        private readonly ConcurrentDictionary<string, HashSet<UnitSummary>> _unitsPerServer;

        private readonly Session _session;

        public UnitCollector(ILogger<EventCollector> logger, Session session)
        {
            _meter = new Meter("Telemachus.Core.Collectors.EventCollector");
            _logger = logger;
            _session = session;

            _unitsPerServer = new();
        }

        public void Execute(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            _logger.LogDebug("Executing EventCollector");
            List<Task> tasks = new();
            foreach (KeyValuePair<string, GrpcChannel> entry in gameServerChannels)
            {
                var serverShortName = entry.Key;
                var grpcChannel = entry.Value;

                tasks.Add(MonitorAsync(serverShortName, grpcChannel, stoppingToken));
                _meter.CreateObservableGauge("units_per_server_gauge",
                    () => { return GetUnitsPerServer(serverShortName); },
                    description: "The number of unit types currently on the server");
            }
        }

        public async Task MonitorAsync(string serverShortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                StreamUnitsResponse? unitUpdate = null;
                try
                {
                    var client = new MissionService.MissionServiceClient(channel);
                    var units = client.StreamUnits(new StreamUnitsRequest { }, cancellationToken: stoppingToken);
                    _unitsPerServer[serverShortName] = new();

                    while (await units.ResponseStream.MoveNext(stoppingToken))
                    {
                        unitUpdate = units.ResponseStream.Current;

                        var tags = new System.Diagnostics.TagList
                        {
                            new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _session.GetSessionId(serverShortName)),
                            new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName)
                        };
                        switch (unitUpdate.UpdateCase)
                        {
                            case StreamUnitsResponse.UpdateOneofCase.None:
                                break;
                            case StreamUnitsResponse.UpdateOneofCase.Unit:
                                var unitEvent = unitUpdate.Unit;
                                var unitSummary = new UnitSummary(unitEvent.Id, unitEvent.Category.ToString());
                                _unitsPerServer[serverShortName].Add(unitSummary);
                                break;
                            case StreamUnitsResponse.UpdateOneofCase.Gone:
                                var goneEvent = unitUpdate.Gone;
                                _unitsPerServer[serverShortName].RemoveWhere(unit => unit.Id == goneEvent.Id);
                                break;
                            default:
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (unitUpdate != null)
                    {
                        _logger.LogError("Exception processing event stream for {event}: {exception}", unitUpdate, ex.Message);
                    }
                    else
                    {
                        _logger.LogError("Exception processing event stream: {exception}", ex.Message);
                    }
                    continue;
                }
            }
        }

        private List<Measurement<int>> GetUnitsPerServer(string serverShortName)
        {
            var units = _unitsPerServer[serverShortName];
            List<Measurement<int>> measurements = new();

            foreach (var category in (GroupCategory[]) Enum.GetValues(typeof(GroupCategory)))
            {
                measurements.Add(new Measurement<int>(units.Count(unit => unit.Category == category.ToString()),
                    new KeyValuePair<string, object?>(ICollector.CATEGORY_LABEL, category),
                    new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName),
                    new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _session.GetSessionId(serverShortName))));
            }

            return measurements;
        }
    }
}
