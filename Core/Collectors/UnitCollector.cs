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
using System.Threading.Channels;
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

        private readonly ILogger<UnitCollector> _logger;
        private readonly string _serverShortName;
        private readonly long _sessionId;
        private readonly GrpcChannel _channel;
        private readonly CancellationToken _stoppingToken;
        
        private readonly Meter _meter;

        private readonly HashSet<UnitSummary> _units;

        public UnitCollector(ILogger<UnitCollector> logger, ICollector.CollectorConfig config)
        {
            _logger = logger;
            _serverShortName = config.ServerShortName;
            _channel = config.Channel;
            _stoppingToken = config.SessionStoppingToken;
            _sessionId = config.SessionId;

            _meter = new Meter("Telemachus.Core.Collectors.UnitCollector");
            
            _units = new();
        }

        public async Task MonitorAsync()
        {
            _meter.CreateObservableGauge("units_per_server_gauge",
                () => { return GetUnits(_serverShortName); },
                description: "The number of unit types currently on the server");

            while (!_stoppingToken.IsCancellationRequested)
            {
                StreamUnitsResponse? unitUpdate = null;
                try
                {
                    var client = new MissionService.MissionServiceClient(_channel);
                    var units = client.StreamUnits(new StreamUnitsRequest {}, cancellationToken: _stoppingToken);

                    while (await units.ResponseStream.MoveNext(_stoppingToken))
                    {
                        unitUpdate = units.ResponseStream.Current;

                        var tags = new System.Diagnostics.TagList
                        {
                            new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _sessionId),
                            new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, _serverShortName)
                        };
                        switch (unitUpdate.UpdateCase)
                        {
                            case StreamUnitsResponse.UpdateOneofCase.None:
                                break;
                            case StreamUnitsResponse.UpdateOneofCase.Unit:
                                var unitEvent = unitUpdate.Unit;
                                var unitSummary = new UnitSummary(unitEvent.Id, unitEvent.Category.ToString());
                                _units.Add(unitSummary);
                                break;
                            case StreamUnitsResponse.UpdateOneofCase.Gone:
                                var goneEvent = unitUpdate.Gone;
                                _units.RemoveWhere(unit => unit.Id == goneEvent.Id);
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
                        _logger.LogError("Exception processing unit stream for {unit}: {exception}", unitUpdate, ex.Message);
                    }
                    else
                    {
                        _logger.LogError("Exception processing unit stream: {exception}", ex.Message);
                    }
                    continue;
                }
            }
            _meter.Dispose();
        }

        private List<Measurement<int>> GetUnits(string serverShortName)
        {
            List<Measurement<int>> measurements = new();

            foreach (var category in (GroupCategory[]) Enum.GetValues(typeof(GroupCategory)))
            {
                measurements.Add(new Measurement<int>(_units.Count(unit => unit.Category == category.ToString()),
                    new KeyValuePair<string, object?>(ICollector.CATEGORY_LABEL, category),
                    new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName),
                    new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _sessionId)));
            }

            return measurements;
        }
    }
}
