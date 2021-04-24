// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Yarp.ReverseProxy.Abstractions.RouteDiscovery.Contract;
using Yarp.ReverseProxy.RuntimeModel;
using Yarp.ReverseProxy.Service.Routing;
using CorsConstants = Yarp.ReverseProxy.Abstractions.RouteDiscovery.Contract.CorsConstants;

namespace Yarp.ReverseProxy
{
    internal sealed class ProxyEndpointFactory
    {
        private static readonly IAuthorizeData _defaultAuthorization = new AuthorizeAttribute();
        private static readonly IEnableCorsAttribute _defaultCors = new EnableCorsAttribute();
        private static readonly IDisableCorsAttribute _disableCors = new DisableCorsAttribute();
        private static readonly IAllowAnonymous _allowAnonymous = new AllowAnonymousAttribute();

        private RequestDelegate _pipeline;

        public Endpoint CreateEndpoint(RouteConfig route, IReadOnlyList<Action<EndpointBuilder>> conventions)
        {
            var proxyRoute = route.ProxyRoute;
            var proxyMatch = proxyRoute.Match;

            // Catch-all pattern when no path was specified
            var pathPattern = string.IsNullOrEmpty(proxyMatch.Path) ? "/{**catchall}" : proxyMatch.Path;

            var endpointBuilder = new RouteEndpointBuilder(
                requestDelegate: _pipeline ?? throw new InvalidOperationException("The pipeline hasn't been provided yet."),
                routePattern: RoutePatternFactory.Parse(pathPattern),
                order: proxyRoute.Order.GetValueOrDefault())
            {
                DisplayName = proxyRoute.RouteId
            };

            endpointBuilder.Metadata.Add(route);

            if (proxyMatch.Hosts != null && proxyMatch.Hosts.Count != 0)
            {
                endpointBuilder.Metadata.Add(new HostAttribute(proxyMatch.Hosts.ToArray()));
            }

            if (proxyRoute.Match.Headers != null && proxyRoute.Match.Headers.Count > 0)
            {
                var matchers = new List<HeaderMatcher>(proxyRoute.Match.Headers.Count);
                foreach (var header in proxyRoute.Match.Headers)
                {
                    matchers.Add(new HeaderMatcher(header.Name, header.Values, header.Mode, header.IsCaseSensitive));
                }

                endpointBuilder.Metadata.Add(new HeaderMetadata(matchers));
            }

            bool acceptCorsPreflight;
            if (string.Equals(CorsConstants.Default, proxyRoute.CorsPolicy, StringComparison.OrdinalIgnoreCase))
            {
                endpointBuilder.Metadata.Add(_defaultCors);
                acceptCorsPreflight = true;
            }
            else if (string.Equals(CorsConstants.Disable, proxyRoute.CorsPolicy, StringComparison.OrdinalIgnoreCase))
            {
                endpointBuilder.Metadata.Add(_disableCors);
                acceptCorsPreflight = true;
            }
            else if (!string.IsNullOrEmpty(proxyRoute.CorsPolicy))
            {
                endpointBuilder.Metadata.Add(new EnableCorsAttribute(proxyRoute.CorsPolicy));
                acceptCorsPreflight = true;
            }
            else
            {
                acceptCorsPreflight = false;
            }

            if (proxyMatch.Methods != null && proxyMatch.Methods.Count > 0)
            {
                endpointBuilder.Metadata.Add(new HttpMethodMetadata(proxyMatch.Methods, acceptCorsPreflight));
            }

            if (string.Equals(AuthorizationConstants.Default, proxyRoute.AuthorizationPolicy, StringComparison.OrdinalIgnoreCase))
            {
                endpointBuilder.Metadata.Add(_defaultAuthorization);
            }
            else if (string.Equals(AuthorizationConstants.Anonymous, proxyRoute.AuthorizationPolicy, StringComparison.OrdinalIgnoreCase))
            {
                endpointBuilder.Metadata.Add(_allowAnonymous);
            }
            else if (!string.IsNullOrEmpty(proxyRoute.AuthorizationPolicy))
            {
                endpointBuilder.Metadata.Add(new AuthorizeAttribute(proxyRoute.AuthorizationPolicy));
            }

            for (var i = 0; i < conventions.Count; i++)
            {
                conventions[i](endpointBuilder);
            }

            return endpointBuilder.Build();
        }

        public void SetProxyPipeline(RequestDelegate pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }
    }
}
