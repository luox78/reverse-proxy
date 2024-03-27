// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Yarp.Sample;

var builder = WebApplication.CreateBuilder(args);

var services = builder.Services;

services.AddControllers();

services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

services.AddHttpContextAccessor();

// Interface that collects general metrics about the proxy forwarder
services.AddMetricsConsumer<ForwarderMetricsConsumer>();

// Registration of a consumer to events for proxy forwarder telemetry
services.AddTelemetryConsumer<ForwarderTelemetryConsumer>();

// Registration of a consumer to events for HttpClient telemetry
services.AddTelemetryConsumer<HttpClientTelemetryConsumer>();

services.AddTelemetryConsumer<WebSocketsTelemetryConsumer>();

var app = builder.Build();

// Custom middleware that collects and reports the proxy metrics
// Placed at the beginning so it is the first and last thing run for each request
app.UsePerRequestMetricCollection();

// Middleware used to intercept the WebSocket connection and collect telemetry exposed to WebSocketsTelemetryConsumer
app.UseWebSocketsTelemetry();

app.MapReverseProxy();

app.Run();
