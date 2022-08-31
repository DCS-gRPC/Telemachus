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

using Microsoft.Extensions.DependencyInjection;
using static RurouniJones.Telemachus.Core.Collectors.ICollector;

namespace RurouniJones.Telemachus.Core.Collectors
{
    public class CollectorFactory {
        private readonly IServiceProvider _serviceProvider;
        public CollectorFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ICollector CreateCollector(CollectorType collectorType, CollectorConfig collectorConfig)
        {
            switch (collectorType)
            {
                case CollectorType.BallisticCollector:
                    return ActivatorUtilities.CreateInstance<BallisticCollector>(_serviceProvider, collectorConfig);
                case CollectorType.EventCollector:
                    return ActivatorUtilities.CreateInstance<EventCollector>(_serviceProvider, collectorConfig);
                case CollectorType.PlayerDetailsCollector:
                    return ActivatorUtilities.CreateInstance<PlayerDetailsCollector>(_serviceProvider, collectorConfig);
                case CollectorType.UnitCollector:
                    return ActivatorUtilities.CreateInstance< UnitCollector>(_serviceProvider, collectorConfig);
                default:
                    throw new Exception("Unrecognised Collector Type");
            };
        }
    }
}
