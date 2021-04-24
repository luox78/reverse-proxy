// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Yarp.ReverseProxy.Telemetry.Consumption
{
#if !NET5_0
    internal interface IKestrelMetricsConsumer { }
#endif

    internal sealed class KestrelEventListenerService : EventListenerService<KestrelEventListenerService, IKestrelTelemetryConsumer, IKestrelMetricsConsumer>
    {
#if NET5_0
        private KestrelMetrics _previousMetrics;
        private KestrelMetrics _currentMetrics = new();
        private int _eventCountersCount;
#endif

        protected override string EventSourceName => "Microsoft-AspNetCore-Server-Kestrel";

        public KestrelEventListenerService(ILogger<KestrelEventListenerService> logger, IEnumerable<IKestrelTelemetryConsumer> telemetryConsumers, IEnumerable<IKestrelMetricsConsumer> metricsConsumers)
            : base(logger, telemetryConsumers, metricsConsumers)
        { }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            const int MinEventId = 3;
            const int MaxEventId = 4;

            if (eventData.EventId < MinEventId || eventData.EventId > MaxEventId)
            {
#if NET5_0
                if (eventData.EventId == -1)
                {
                    OnEventCounters(eventData);
                }
#endif

                return;
            }

            if (TelemetryConsumers is null)
            {
                return;
            }

            var payload = eventData.Payload;

#if NET5_0
            switch (eventData.EventId)
            {
                case 3:
                    Debug.Assert(eventData.EventName == "RequestStart" && payload.Count == 5);
                    {
                        var connectionId = (string)payload[0];
                        var requestId = (string)payload[1];
                        var httpVersion = (string)payload[2];
                        var path = (string)payload[3];
                        var method = (string)payload[4];
                        foreach (var consumer in TelemetryConsumers)
                        {
                            consumer.OnRequestStart(eventData.TimeStamp, connectionId, requestId, httpVersion, path, method);
                        }
                    }
                    break;

                case 4:
                    Debug.Assert(eventData.EventName == "RequestStop" && payload.Count == 5);
                    {
                        var connectionId = (string)payload[0];
                        var requestId = (string)payload[1];
                        var httpVersion = (string)payload[2];
                        var path = (string)payload[3];
                        var method = (string)payload[4];
                        foreach (var consumer in TelemetryConsumers)
                        {
                            consumer.OnRequestStop(eventData.TimeStamp, connectionId, requestId, httpVersion, path, method);
                        }
                    }
                    break;
            }
#else
            switch (eventData.EventId)
            {
                case 3:
                    Debug.Assert(eventData.EventName == "RequestStart" && payload.Count == 2);
                    {
                        var connectionId = (string)payload[0];
                        var requestId = (string)payload[1];
                        foreach (var consumer in TelemetryConsumers)
                        {
                            consumer.OnRequestStart(eventData.TimeStamp, connectionId, requestId);
                        }
                    }
                    break;

                case 4:
                    Debug.Assert(eventData.EventName == "RequestStop" && payload.Count == 2);
                    {
                        var connectionId = (string)payload[0];
                        var requestId = (string)payload[1];
                        foreach (var consumer in TelemetryConsumers)
                        {
                            consumer.OnRequestStop(eventData.TimeStamp, connectionId, requestId);
                        }
                    }
                    break;
            }
#endif
        }

#if NET5_0
        private void OnEventCounters(EventWrittenEventArgs eventData)
        {
            if (MetricsConsumers is null)
            {
                return;
            }

            Debug.Assert(eventData.EventName == "EventCounters" && eventData.Payload.Count == 1);
            var counters = (IDictionary<string, object>)eventData.Payload[0];

            if (!counters.TryGetValue("Mean", out var valueObj))
            {
                valueObj = counters["Increment"];
            }

            var value = (long)(double)valueObj;
            var metrics = _currentMetrics;

            switch ((string)counters["Name"])
            {
                case "connections-per-second":
                    metrics.ConnectionRate = value;
                    break;

                case "total-connections":
                    metrics.TotalConnections = value;
                    break;

                case "tls-handshakes-per-second":
                    metrics.TlsHandshakeRate = value;
                    break;

                case "total-tls-handshakes":
                    metrics.TotalTlsHandshakes = value;
                    break;

                case "current-tls-handshakes":
                    metrics.CurrentTlsHandshakes = value;
                    break;

                case "failed-tls-handshakes":
                    metrics.FailedTlsHandshakes = value;
                    break;

                case "current-connections":
                    metrics.CurrentConnections = value;
                    break;

                case "connection-queue-length":
                    metrics.ConnectionQueueLength = value;
                    break;

                case "request-queue-length":
                    metrics.RequestQueueLength = value;
                    break;

                case "current-upgraded-requests":
                    metrics.CurrentUpgradedRequests = value;
                    break;

                default:
                    return;
            }

            const int TotalEventCounters = 10;

            if (++_eventCountersCount == TotalEventCounters)
            {
                _eventCountersCount = 0;

                metrics.Timestamp = DateTime.UtcNow;

                var previous = _previousMetrics;
                _previousMetrics = metrics;
                _currentMetrics = new KestrelMetrics();

                if (previous is null)
                {
                    return;
                }

                try
                {
                    foreach (var consumer in MetricsConsumers)
                    {
                        consumer.OnKestrelMetrics(previous, metrics);
                    }
                }
                catch (Exception ex)
                {
                    // We can't let an uncaught exception propagate as that would crash the process
                    Logger.LogError(ex, $"Uncaught exception occured while processing {nameof(KestrelMetrics)}.");
                }
            }
        }
#endif
    }
}
