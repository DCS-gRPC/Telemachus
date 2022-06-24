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
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Common;
using RurouniJones.Dcs.Grpc.V0.Mission;

namespace RurouniJones.Telemachus.Core.Collectors
{
    public class EventCollector : ICollector
    {

        private readonly ILogger<EventCollector> _logger;

        private Meter _meter;

        private readonly Counter<int> _shootCounter;
        private readonly Counter<int> _takeoffCounter;
        private readonly Counter<int> _landingCounter;
        private readonly Counter<int> _crashCounter;


        public EventCollector(ILogger<EventCollector> logger)
        {
            _meter = new Meter("Telemachus.Core.Collectors.EventCollector");
            _logger = logger;

            _shootCounter = _meter.CreateCounter<int>("shoot_counter", "shots", "Number of shots");
            _takeoffCounter = _meter.CreateCounter<int>("takeoff_counter", "takeoffs", "Number of takeoffs");
            _landingCounter = _meter.CreateCounter<int>("landing_counter", "landings", "Number of landings");
            _crashCounter = _meter.CreateCounter<int>("crash_counter", "crashes", "Number of crashes");
        }

        public void Execute(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            List<Task> tasks = new();
            foreach (KeyValuePair<string, GrpcChannel> entry in gameServerChannels)
            {
                tasks.Add(MonitorAsync(entry.Key, entry.Value, stoppingToken));
            }
        }

        public async Task MonitorAsync(string serverShortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            while(!stoppingToken.IsCancellationRequested) {
                StreamEventsResponse? eventUpdate = null;
                try
                { 
                    var client = new MissionService.MissionServiceClient(channel);
                    var events = client.StreamEvents(new StreamEventsRequest {}, cancellationToken: stoppingToken);

                    while (await events.ResponseStream.MoveNext(stoppingToken))
                    {
                        eventUpdate = events.ResponseStream.Current;

                        var tags = new System.Diagnostics.TagList
                        {
                            new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName)
                        };
                        switch (eventUpdate.EventCase)
                        {
                            case StreamEventsResponse.EventOneofCase.None:
                                break;
                            case StreamEventsResponse.EventOneofCase.Shot:
                                var shotEvent = eventUpdate.Shot;
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_TYPE_LABEL, shotEvent.Initiator.Unit.Type));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_COALITION_LABEL, shotEvent.Initiator.Unit.Coalition));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_IS_PLAYER_LABEL, shotEvent.Initiator.Unit.HasPlayerName));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_CATEGORY_LABEL, shotEvent.Initiator.Unit.Category));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.WEAPON_LABEL, shotEvent.Weapon.Type));
                                _shootCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Hit:
                                //TODO
                                break;
                            case StreamEventsResponse.EventOneofCase.Takeoff:
                                var takeoffEvent = eventUpdate.Takeoff;
                                tags = StandardSingleUnitEventTags(tags, takeoffEvent.Initiator.Unit);
                                if(takeoffEvent.Place != null && takeoffEvent.Place.Name != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_LABEL, takeoffEvent.Place.Name));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_CATEGORY_LABEL, takeoffEvent.Place.Category));

                                }
                                _takeoffCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Land:
                                var landEvent = eventUpdate.Land;
                                tags = StandardSingleUnitEventTags(tags, landEvent.Initiator.Unit);
                                if (landEvent.Place != null && landEvent.Place.Name != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_LABEL, landEvent.Place.Name));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_CATEGORY_LABEL, landEvent.Place.Category));
                                }
                                _landingCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Crash:
                                var crashEvent = eventUpdate.Crash;
                                tags = StandardSingleUnitEventTags(tags, crashEvent.Initiator.Unit);
                                _crashCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Ejection:
                                break;
                            case StreamEventsResponse.EventOneofCase.Refueling:
                                break;
                            case StreamEventsResponse.EventOneofCase.Dead:
                                break;
                            case StreamEventsResponse.EventOneofCase.PilotDead:
                                break;
                            case StreamEventsResponse.EventOneofCase.BaseCapture:
                                break;
                            case StreamEventsResponse.EventOneofCase.MissionStart:
                                break;
                            case StreamEventsResponse.EventOneofCase.MissionEnd:
                                break;
                            case StreamEventsResponse.EventOneofCase.RefuelingStop:
                                break;
                            case StreamEventsResponse.EventOneofCase.Birth:
                                break;
                            case StreamEventsResponse.EventOneofCase.HumanFailure:
                                break;
                            case StreamEventsResponse.EventOneofCase.DetailedFailure:
                                break;
                            case StreamEventsResponse.EventOneofCase.EngineStartup:
                                break;
                            case StreamEventsResponse.EventOneofCase.EngineShutdown:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerEnterUnit:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerLeaveUnit:
                                break;
                            case StreamEventsResponse.EventOneofCase.ShootingStart:
                                break;
                            case StreamEventsResponse.EventOneofCase.ShootingEnd:
                                break;
                            case StreamEventsResponse.EventOneofCase.MarkAdd:
                                break;
                            case StreamEventsResponse.EventOneofCase.MarkChange:
                                break;
                            case StreamEventsResponse.EventOneofCase.MarkRemove:
                                break;
                            case StreamEventsResponse.EventOneofCase.Kill:
                                break;
                            case StreamEventsResponse.EventOneofCase.Score:
                                break;
                            case StreamEventsResponse.EventOneofCase.UnitLost:
                                break;
                            case StreamEventsResponse.EventOneofCase.LandingAfterEjection:
                                break;
                            case StreamEventsResponse.EventOneofCase.DiscardChairAfterEjection:
                                break;
                            case StreamEventsResponse.EventOneofCase.WeaponAdd:
                                break;
                            case StreamEventsResponse.EventOneofCase.LandingQualityMark:
                                break;
                            case StreamEventsResponse.EventOneofCase.Connect:
                                break;
                            case StreamEventsResponse.EventOneofCase.Disconnect:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerSendChat:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerChangeSlot:
                                break;
                            case StreamEventsResponse.EventOneofCase.MissionCommand:
                                break;
                            case StreamEventsResponse.EventOneofCase.CoalitionCommand:
                                break;
                            case StreamEventsResponse.EventOneofCase.GroupCommand:
                                break;
                            default:
                                break;
                        }
                    }
                } catch (Exception ex) {
                    if(eventUpdate != null) { 
                        _logger.LogError($"Exception processing event stream for {eventUpdate}", ex);
                    } else
                    {
                        _logger.LogError($"Exception processing event stream", ex);
                    }
                    continue;
                }
            }
        }

        public static System.Diagnostics.TagList StandardSingleUnitEventTags(System.Diagnostics.TagList tags, Unit unit)
        {
            tags.Add(new KeyValuePair<string, object?>(ICollector.AIRCRAFT_TYPE_LABEL, unit.Type));
            tags.Add(new KeyValuePair<string, object?>(ICollector.COALITION_LABEL, unit.Coalition));
            tags.Add(new KeyValuePair<string, object?>(ICollector.IS_PLAYER_LABEL, unit.HasPlayerName));
            tags.Add(new KeyValuePair<string, object?>(ICollector.CATEGORY_LABEL, unit.Category));
            return tags;
        }
    }
}
