// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;

namespace Yarp.ReverseProxy.Telemetry.Consumption
{
    internal sealed class NetSecurityEventListenerService : EventListenerService<NetSecurityEventListenerService, INetSecurityTelemetryConsumer, INetSecurityMetricsConsumer>
    {
        private NetSecurityMetrics _previousMetrics;
        private NetSecurityMetrics _currentMetrics = new();
        private int _eventCountersCount;

        protected override string EventSourceName => "System.Net.Security";

        public NetSecurityEventListenerService(ILogger<NetSecurityEventListenerService> logger, IEnumerable<INetSecurityTelemetryConsumer> telemetryConsumers, IEnumerable<INetSecurityMetricsConsumer> metricsConsumers)
            : base(logger, telemetryConsumers, metricsConsumers)
        { }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            const int MinEventId = 1;
            const int MaxEventId = 3;

            if (eventData.EventId < MinEventId || eventData.EventId > MaxEventId)
            {
                if (eventData.EventId == -1)
                {
                    OnEventCounters(eventData);
                }

                return;
            }

            if (TelemetryConsumers is null)
            {
                return;
            }

            var payload = eventData.Payload;

            switch (eventData.EventId)
            {
                case 1:
                    Debug.Assert(eventData.EventName == "HandshakeStart" && payload.Count == 2);
                    {
                        var isServer = (bool)payload[0];
                        var targetHost = (string)payload[1];
                        foreach (var consumer in TelemetryConsumers)
                        {
                            consumer.OnHandshakeStart(eventData.TimeStamp, isServer, targetHost);
                        }
                    }
                    break;

                case 2:
                    Debug.Assert(eventData.EventName == "HandshakeStop" && payload.Count == 1);
                    {
                        var protocol = (SslProtocols)payload[0];
                        foreach (var consumer in TelemetryConsumers)
                        {
                            consumer.OnHandshakeStop(eventData.TimeStamp, protocol);
                        }
                    }
                    break;

                case 3:
                    Debug.Assert(eventData.EventName == "HandshakeFailed" && payload.Count == 3);
                    {
                        var isServer = (bool)payload[0];
                        var elapsed = TimeSpan.FromMilliseconds((double)payload[1]);
                        var exceptionMessage = (string)payload[2];
                        foreach (var consumer in TelemetryConsumers)
                        {
                            consumer.OnHandshakeFailed(eventData.TimeStamp, isServer, elapsed, exceptionMessage);
                        }
                    }
                    break;
            }
        }

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

            var value = (double)valueObj;
            var metrics = _currentMetrics;

            switch ((string)counters["Name"])
            {
                case "tls-handshake-rate":
                    metrics.TlsHandshakeRate = (long)value;
                    break;

                case "total-tls-handshakes":
                    metrics.TotalTlsHandshakes = (long)value;
                    break;

                case "current-tls-handshakes":
                    metrics.CurrentTlsHandshakes = (long)value;
                    break;

                case "failed-tls-handshakes":
                    metrics.FailedTlsHandshakes = (long)value;
                    break;

                case "all-tls-sessions-open":
                    metrics.TlsSessionsOpen = (long)value;
                    break;

                case "tls10-sessions-open":
                    metrics.Tls10SessionsOpen = (long)value;
                    break;

                case "tls11-sessions-open":
                    metrics.Tls11SessionsOpen = (long)value;
                    break;

                case "tls12-sessions-open":
                    metrics.Tls12SessionsOpen = (long)value;
                    break;

                case "tls13-sessions-open":
                    metrics.Tls13SessionsOpen = (long)value;
                    break;

                case "all-tls-handshake-duration":
                    metrics.TlsHandshakeDuration = TimeSpan.FromMilliseconds(value);
                    break;

                case "tls10-handshake-duration":
                    metrics.Tls10HandshakeDuration = TimeSpan.FromMilliseconds(value);
                    break;

                case "tls11-handshake-duration":
                    metrics.Tls11HandshakeDuration = TimeSpan.FromMilliseconds(value);
                    break;

                case "tls12-handshake-duration":
                    metrics.Tls12HandshakeDuration = TimeSpan.FromMilliseconds(value);
                    break;

                case "tls13-handshake-duration":
                    metrics.Tls13HandshakeDuration = TimeSpan.FromMilliseconds(value);
                    break;

                default:
                    return;
            }

            const int TotalEventCounters = 14;

            if (++_eventCountersCount == TotalEventCounters)
            {
                _eventCountersCount = 0;

                metrics.Timestamp = DateTime.UtcNow;

                var previous = _previousMetrics;
                _previousMetrics = metrics;
                _currentMetrics = new NetSecurityMetrics();

                if (previous is null)
                {
                    return;
                }

                try
                {
                    foreach (var consumer in MetricsConsumers)
                    {
                        consumer.OnNetSecurityMetrics(previous, metrics);
                    }
                }
                catch (Exception ex)
                {
                    // We can't let an uncaught exception propagate as that would crash the process
                    Logger.LogError(ex, $"Uncaught exception occured while processing {nameof(NetSecurityMetrics)}.");
                }
            }
        }
    }
}
