using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yarp.ReverseProxy.Telemetry.Consumption
{
    public class ProxyRequestMetrics
    {
        public long RequestBytes { get; internal set; }
        public long RequestOps { get; internal set; }

        public long ResponseBytes { get; internal set; }
        public long ResponseOps { get; internal set; }

        public DateTime TimeProxyRequestStarted { get; internal set; }
        public DateTime TimeDestinationRequestStarted { get; internal set; }
        public DateTime TimeDestinationConnectionCreated { get; internal set; }
        public DateTime TimeDestinationRequestLeftQueue { get; internal set; }
        public DateTime TimeDestinationRequestHeadersStart { get; internal set; }
        public DateTime TimeDestinationRequestHeadersStop { get; internal set; }
        public DateTime TimeDestinationRequestContentStart { get; internal set; }
        public DateTime TimeDestinationRequestContentStop { get; internal set; }
        public DateTime TimeDestinationResponseHeadersStart { get; internal set; }
        public DateTime TimeDestinationResponseHeadersStop { get; internal set; }
        public DateTime TimeDestinationResponseContentStart { get; internal set; }
        public DateTime TimeDestinationResponseContentStop { get; internal set; }
        public DateTime TimeProxyRequestStop { get; internal set; }
    }
}
