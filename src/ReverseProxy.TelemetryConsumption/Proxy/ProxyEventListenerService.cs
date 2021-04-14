// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Service.Proxy;

namespace Yarp.ReverseProxy.Telemetry.Consumption
{
    internal sealed class ProxyEventListenerService : EventListener, IHostedService
    {
        private readonly ILogger<ProxyEventListenerService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private ProxyMetrics _previousMetrics;
        private ProxyMetrics _currentMetrics = new();
        private int _eventCountersCount;

        public ProxyEventListenerService(ILogger<ProxyEventListenerService> logger, IServiceProvider serviceProvider, IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Yarp.ReverseProxy")
            {
                var arguments = new Dictionary<string, string> { { "EventCounterIntervalSec", MetricsOptions.Interval.TotalSeconds.ToString() } };
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.None, arguments);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            const int MinEventId = 1;
            const int MaxEventId = 7;
            var hasTelemetryConsumers = false;

            if (eventData.EventId < MinEventId || eventData.EventId > MaxEventId)
            {
                if (eventData.EventId == -1)
                {
                    OnEventCounters(eventData);
                }

                return;
            }

            var context = _httpContextAccessor?.HttpContext;
            if (context is null)
            {
                return;
            }

            using var consumers = context.RequestServices.GetServices<IProxyTelemetryConsumer>().GetEnumerator();
            var perRequestMetrics = context.Features.Get<ProxyRequestMetrics>();

            if (!consumers.MoveNext())
            {
                if (perRequestMetrics == null) { return; }
            }
            else
            {
                hasTelemetryConsumers = true;
            }

            var payload = eventData.Payload;

            switch (eventData.EventId)
            {
                case 1:
                    Debug.Assert(eventData.EventName == "ProxyStart" && payload.Count == 1);
                    {
                        var destinationPrefix = (string)payload[0];
                        if (hasTelemetryConsumers)
                        {
                            do
                            {
                                consumers.Current.OnProxyStart(eventData.TimeStamp, destinationPrefix);
                            }
                            while (consumers.MoveNext());
                        }
                        if (perRequestMetrics != null)
                        {
                            perRequestMetrics.TimeProxyRequestStarted = eventData.TimeStamp;
                        }
                    }
                    break;

                case 2:
                    Debug.Assert(eventData.EventName == "ProxyStop" && payload.Count == 1);
                    {
                        var statusCode = (int)payload[0];
                        if (hasTelemetryConsumers)
                        {
                            do
                            {
                                consumers.Current.OnProxyStop(eventData.TimeStamp, statusCode);
                            }
                            while (consumers.MoveNext());
                        }
                        if (perRequestMetrics != null)
                        {
                            perRequestMetrics.TimeProxyRequestStop = eventData.TimeStamp;
                        }
                    }
                    break;

                case 3:
                    Debug.Assert(eventData.EventName == "ProxyFailed" && payload.Count == 1);
                    {
                        var error = (ProxyError)payload[0];
                        do
                        {
                            consumers.Current.OnProxyFailed(eventData.TimeStamp, error);
                        }
                        while (consumers.MoveNext());
                    }
                    break;

                case 4:
                    Debug.Assert(eventData.EventName == "ProxyStage" && payload.Count == 1);
                    {
                        var proxyStage = (ProxyStage)payload[0];
                        do
                        {
                            consumers.Current.OnProxyStage(eventData.TimeStamp, proxyStage);
                        }
                        while (consumers.MoveNext());
                    }
                    break;

                case 5:
                    Debug.Assert(eventData.EventName == "ContentTransferring" && payload.Count == 5);
                    {
                        var isRequest = (bool)payload[0];
                        var contentLength = (long)payload[1];
                        var iops = (long)payload[2];
                        var readTime = new TimeSpan((long)payload[3]);
                        var writeTime = new TimeSpan((long)payload[4]);
                        do
                        {
                            consumers.Current.OnContentTransferring(eventData.TimeStamp, isRequest, contentLength, iops, readTime, writeTime);
                        }
                        while (consumers.MoveNext());
                    }
                    break;

                case 6:
                    Debug.Assert(eventData.EventName == "ContentTransferred" && payload.Count == 6);
                    {
                        var isRequest = (bool)payload[0];
                        var contentLength = (long)payload[1];
                        var iops = (long)payload[2];
                        var readTime = new TimeSpan((long)payload[3]);
                        var writeTime = new TimeSpan((long)payload[4]);
                        var firstReadTime = new TimeSpan((long)payload[5]);
                        if (hasTelemetryConsumers)
                        {
                            do
                            {
                                consumers.Current.OnContentTransferred(eventData.TimeStamp, isRequest, contentLength, iops, readTime, writeTime, firstReadTime);
                            }
                            while (consumers.MoveNext());
                        }
                        if (perRequestMetrics != null)
                        {
                            if (isRequest)
                            {
                                perRequestMetrics.RequestBytes = contentLength;
                                perRequestMetrics.RequestOps = iops;
                            }
                            else
                            {
                                perRequestMetrics.ResponseBytes = contentLength;
                                perRequestMetrics.ResponseOps = iops;
                                perRequestMetrics.TimeDestinationResponseContentStart = eventData.TimeStamp.AddTicks(-(long)payload[4]);
                                perRequestMetrics.TimeDestinationResponseContentStop = eventData.TimeStamp;
                            }
                        }
                    }
                    break;

                case 7:
                    Debug.Assert(eventData.EventName == "ProxyInvoke" && payload.Count == 3);
                    {
                        var clusterId = (string)payload[0];
                        var routeId = (string)payload[1];
                        var destinationId = (string)payload[2];
                        do
                        {
                            consumers.Current.OnProxyInvoke(eventData.TimeStamp, clusterId, routeId, destinationId);
                        }
                        while (consumers.MoveNext());
                    }
                    break;
            }
        }

        private void OnEventCounters(EventWrittenEventArgs eventData)
        {
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
                case "requests-started":
                    metrics.RequestsStarted = value;
                    break;

                case "requests-started-rate":
                    metrics.RequestsStartedRate = value;
                    break;

                case "requests-failed":
                    metrics.RequestsFailed = value;
                    break;

                case "current-requests":
                    metrics.CurrentRequests = value;
                    break;

                default:
                    return;
            }

            const int TotalEventCounters = 4;

            if (++_eventCountersCount == TotalEventCounters)
            {
                _eventCountersCount = 0;

                metrics.Timestamp = DateTime.UtcNow;

                var previous = _previousMetrics;
                _previousMetrics = metrics;
                _currentMetrics = new ProxyMetrics();

                if (previous is null || _serviceProvider is null)
                {
                    return;
                }

                try
                {
                    foreach (var consumer in _serviceProvider.GetServices<IProxyMetricsConsumer>())
                    {
                        consumer.OnProxyMetrics(previous, metrics);
                    }
                }
                catch (Exception ex)
                {
                    // We can't let an uncaught exception propagate as that would crash the process
                    _logger.LogError(ex, $"Uncaught exception occured while processing {nameof(ProxyMetrics)}.");
                }
            }
        }
    }
}
