// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;
using Yarp.ReverseProxy.Abstractions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.RuntimeModel;
using Yarp.ReverseProxy.Service.HealthChecks;
using Yarp.ReverseProxy.Service.Proxy;
using Yarp.ReverseProxy.Utilities;
using Yarp.ReverseProxy.Utilities.Tests;

namespace Yarp.ReverseProxy.Service.Management.Tests
{
    public class ProxyConfigManagerTests
    {
        private IServiceProvider CreateServices(List<ProxyRoute> routes, List<Cluster> clusters, Action<IReverseProxyBuilder> configureProxy = null)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddRouting();
            var proxyBuilder = serviceCollection.AddReverseProxy().LoadFromMemory(routes, clusters);
            serviceCollection.TryAddSingleton(new Mock<IWebHostEnvironment>().Object);
            var activeHealthPolicy = new Mock<IActiveHealthCheckPolicy>();
            activeHealthPolicy.SetupGet(p => p.Name).Returns("activePolicyA");
            serviceCollection.AddSingleton(activeHealthPolicy.Object);
            configureProxy?.Invoke(proxyBuilder);
            var services = serviceCollection.BuildServiceProvider();
            var routeBuilder = services.GetRequiredService<ProxyEndpointFactory>();
            routeBuilder.SetProxyPipeline(context => Task.CompletedTask);
            return services;
        }

        [Fact]
        public void Constructor_Works()
        {
            var services = CreateServices(new List<ProxyRoute>(), new List<Cluster>());
            _ = services.GetRequiredService<ProxyConfigManager>();
        }

        [Fact]
        public async Task NullRoutes_StartsEmpty()
        {
            var services = CreateServices(null, new List<Cluster>());
            var manager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await manager.InitialLoadAsync();
            Assert.NotNull(dataSource);
            var endpoints = dataSource.Endpoints;
            Assert.Empty(endpoints);
        }

        [Fact]
        public async Task NullClusters_StartsEmpty()
        {
            var services = CreateServices(new List<ProxyRoute>(), null);
            var manager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await manager.InitialLoadAsync();
            Assert.NotNull(dataSource);
            var endpoints = dataSource.Endpoints;
            Assert.Empty(endpoints);
        }

        [Fact]
        public async Task Endpoints_StartsEmpty()
        {
            var services = CreateServices(new List<ProxyRoute>(), new List<Cluster>());
            var manager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await manager.InitialLoadAsync();
            Assert.NotNull(dataSource);
            var endpoints = dataSource.Endpoints;
            Assert.Empty(endpoints);
        }

        [Fact]
        public async Task GetChangeToken_InitialValue()
        {
            var services = CreateServices(new List<ProxyRoute>(), new List<Cluster>());
            var manager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await manager.InitialLoadAsync();
            Assert.NotNull(dataSource);
            var changeToken = dataSource.GetChangeToken();
            Assert.NotNull(changeToken);
            Assert.True(changeToken.ActiveChangeCallbacks);
            Assert.False(changeToken.HasChanged);
        }

        [Fact]
        public async Task BuildConfig_OneClusterOneDestinationOneRoute_Works()
        {
            const string TestAddress = "https://localhost:123/";

            var cluster = new Cluster
            {
                Id = "cluster1",
                Destinations = new Dictionary<string, Destination>(StringComparer.OrdinalIgnoreCase)
                {
                    { "d1", new Destination { Address = TestAddress } }
                }
            };
            var route = new ProxyRoute
            {
                RouteId = "route1",
                ClusterId = "cluster1",
                Match = new RouteMatch { Path = "/" }
            };

            var services = CreateServices(new List<ProxyRoute>() { route }, new List<Cluster>() { cluster });

            var manager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await manager.InitialLoadAsync();

            Assert.NotNull(dataSource);
            var endpoints = dataSource.Endpoints;
            var endpoint = Assert.Single(endpoints);
            var routeConfig = endpoint.Metadata.GetMetadata<RouteConfig>();
            Assert.NotNull(routeConfig);
            Assert.Equal("route1", routeConfig.ProxyRoute.RouteId);

            var clusterInfo = routeConfig.Cluster;
            Assert.NotNull(clusterInfo);

            Assert.Equal("cluster1", clusterInfo.ClusterId);
            Assert.NotNull(clusterInfo.Destinations);
            Assert.NotNull(clusterInfo.Config);
            Assert.NotNull(clusterInfo.Config.HttpClient);
            Assert.Same(clusterInfo, routeConfig.Cluster);

            var actualDestinations = clusterInfo.Destinations.Values;
            var destination = Assert.Single(actualDestinations);
            Assert.Equal("d1", destination.DestinationId);
            Assert.NotNull(destination.Config);
            Assert.Equal(TestAddress, destination.Config.Options.Address);
        }

        [Fact]
        public async Task InitialLoadAsync_ProxyHttpClientOptionsSet_CreateAndSetHttpClient()
        {
            const string TestAddress = "https://localhost:123/";

            var clientCertificate = TestResources.GetTestCertificate();
            var cluster = new Cluster
            {
                Id = "cluster1",
                Destinations = new Dictionary<string, Destination>(StringComparer.OrdinalIgnoreCase)
                {
                    { "d1", new Destination { Address = TestAddress } }
                },
                HttpClient = new ProxyHttpClientOptions
                {
                    SslProtocols = SslProtocols.Tls11 | SslProtocols.Tls12,
                    MaxConnectionsPerServer = 10,
                    ClientCertificate = clientCertificate,
#if NET
                    RequestHeaderEncoding = Encoding.UTF8
#endif
                }
            };
            var route = new ProxyRoute
            {
                RouteId = "route1",
                ClusterId = "cluster1",
                Match = new RouteMatch { Path = "/" }
            };

            var services = CreateServices(new List<ProxyRoute>() { route }, new List<Cluster>() { cluster });

            var manager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await manager.InitialLoadAsync();

            Assert.NotNull(dataSource);
            var endpoint = Assert.Single(dataSource.Endpoints);
            var routeConfig = endpoint.Metadata.GetMetadata<RouteConfig>();
            var clusterInfo = routeConfig.Cluster;
            Assert.Equal("cluster1", clusterInfo.ClusterId);
            var clusterConfig = clusterInfo.Config;
            Assert.NotNull(clusterConfig.HttpClient);
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, clusterConfig.Options.HttpClient.SslProtocols);
            Assert.Equal(10, clusterConfig.Options.HttpClient.MaxConnectionsPerServer);
            Assert.Same(clientCertificate, clusterConfig.Options.HttpClient.ClientCertificate);
#if NET
            Assert.Equal(Encoding.UTF8, clusterConfig.Options.HttpClient.RequestHeaderEncoding);
#endif

            var handler = Proxy.Tests.ProxyHttpClientFactoryTests.GetHandler(clusterConfig.HttpClient);
            Assert.Equal(SslProtocols.Tls11 | SslProtocols.Tls12, handler.SslOptions.EnabledSslProtocols);
            Assert.Equal(10, handler.MaxConnectionsPerServer);
            Assert.Single(handler.SslOptions.ClientCertificates, clientCertificate);
#if NET
            Assert.Equal(Encoding.UTF8, handler.RequestHeaderEncodingSelector(default, default));
#endif
        }

        [Fact]
        public async Task GetChangeToken_SignalsChange()
        {
            var services = CreateServices(new List<ProxyRoute>(), new List<Cluster>());
            var inMemoryConfig = (InMemoryConfigProvider)services.GetRequiredService<IProxyConfigProvider>();
            var configManager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await configManager.InitialLoadAsync();
            _ = configManager.Endpoints; // Lazily creates endpoints the first time, activates change notifications.

            var signaled1 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var signaled2 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            IReadOnlyList<Endpoint> readEndpoints1 = null;
            IReadOnlyList<Endpoint> readEndpoints2 = null;

            var changeToken1 = dataSource.GetChangeToken();
            changeToken1.RegisterChangeCallback(
                _ =>
                {
                    readEndpoints1 = dataSource.Endpoints;
                    signaled1.SetResult(1);
                }, null);

            // updating should signal the current change token
            Assert.False(signaled1.Task.IsCompleted);
            inMemoryConfig.Update(new List<ProxyRoute>() { new ProxyRoute() { RouteId = "r1", Match = new RouteMatch { Path = "/" } } }, new List<Cluster>());
            await signaled1.Task.DefaultTimeout();

            var changeToken2 = dataSource.GetChangeToken();
            changeToken2.RegisterChangeCallback(
                _ =>
                {
                    readEndpoints2 = dataSource.Endpoints;
                    signaled2.SetResult(1);
                }, null);

            // updating again should only signal the new change token
            Assert.False(signaled2.Task.IsCompleted);
            inMemoryConfig.Update(new List<ProxyRoute>() { new ProxyRoute() { RouteId = "r2", Match = new RouteMatch { Path = "/" } } }, new List<Cluster>());
            await signaled2.Task.DefaultTimeout();

            Assert.NotNull(readEndpoints1);
            Assert.NotNull(readEndpoints2);
        }

        [Fact]
        public async Task LoadAsync_RequestVersionValidationError_Throws()
        {
            const string TestAddress = "https://localhost:123/";

            var cluster = new Cluster
            {
                Id = "cluster1",
                Destinations = new Dictionary<string, Destination>(StringComparer.OrdinalIgnoreCase)
                {
                    { "d1", new Destination { Address = TestAddress } }
                },
                HttpRequest = new RequestProxyOptions() { Version = new Version(1, 2) }
            };

            var services = CreateServices(new List<ProxyRoute>(), new List<Cluster>() { cluster });
            var configManager = services.GetRequiredService<ProxyConfigManager>();

            var ioEx = await Assert.ThrowsAsync<InvalidOperationException>(() => configManager.InitialLoadAsync());
            Assert.Equal("Unable to load or apply the proxy configuration.", ioEx.Message);
            var agex = Assert.IsType<AggregateException>(ioEx.InnerException);

            Assert.Single(agex.InnerExceptions);
            var argex = Assert.IsType<ArgumentException>(agex.InnerExceptions.First());
            Assert.StartsWith("Outgoing request version", argex.Message);
        }

        [Fact]
        public async Task LoadAsync_RouteValidationError_Throws()
        {
            var routeName = "route1";
            var route1 = new ProxyRoute { RouteId = routeName, Match = new RouteMatch { Hosts = null }, ClusterId = "cluster1" };
            var services = CreateServices(new List<ProxyRoute>() { route1 }, new List<Cluster>());
            var configManager = services.GetRequiredService<ProxyConfigManager>();

            var ioEx = await Assert.ThrowsAsync<InvalidOperationException>(() => configManager.InitialLoadAsync());
            Assert.Equal("Unable to load or apply the proxy configuration.", ioEx.Message);
            var agex = Assert.IsType<AggregateException>(ioEx.InnerException);

            Assert.Single(agex.InnerExceptions);
            var argex = Assert.IsType<ArgumentException>(agex.InnerExceptions.First());
            Assert.StartsWith($"Route '{routeName}' requires Hosts or Path specified", argex.Message);
        }

        [Fact]
        public async Task LoadAsync_ConfigFilterRouteActions_CanFixBrokenRoute()
        {
            var route1 = new ProxyRoute { RouteId = "route1", Match = new RouteMatch { Hosts = null }, Order = 1, ClusterId = "cluster1" };
            var services = CreateServices(new List<ProxyRoute>() { route1 }, new List<Cluster>(), proxyBuilder =>
            {
                proxyBuilder.AddConfigFilter<FixRouteHostFilter>();
            });
            var configManager = services.GetRequiredService<ProxyConfigManager>();

            var dataSource = await configManager.InitialLoadAsync();
            var endpoints = dataSource.Endpoints;

            Assert.Single(endpoints);
            var endpoint = endpoints.Single();
            Assert.Same(route1.RouteId, endpoint.DisplayName);
            var hostMetadata = endpoint.Metadata.GetMetadata<HostAttribute>();
            Assert.NotNull(hostMetadata);
            var host = Assert.Single(hostMetadata.Hosts);
            Assert.Equal("example.com", host);
        }

        private class FixRouteHostFilter : IProxyConfigFilter
        {
            public ValueTask<Cluster> ConfigureClusterAsync(Cluster cluster, CancellationToken cancel)
            {
                return new ValueTask<Cluster>(cluster);
            }

            public ValueTask<ProxyRoute> ConfigureRouteAsync(ProxyRoute route, CancellationToken cancel)
            {
                return new ValueTask<ProxyRoute>(route with
                {
                    Match = route.Match with { Hosts = new[] { "example.com" } }
                });
            }
        }

        private class ClusterAndRouteFilter : IProxyConfigFilter
        {
            public ValueTask<Cluster> ConfigureClusterAsync(Cluster cluster, CancellationToken cancel)
            {
                return new ValueTask<Cluster>(cluster with
                {
                    HealthCheck = new HealthCheckOptions()
                    {
                        Active = new ActiveHealthCheckOptions { Enabled = true, Interval = TimeSpan.FromSeconds(12), Policy = "activePolicyA" }
                    }
                });
            }

            public ValueTask<ProxyRoute> ConfigureRouteAsync(ProxyRoute route, CancellationToken cancel)
            {
                return new ValueTask<ProxyRoute>(route with { Order = 12 });
            }
        }

        [Fact]
        public async Task LoadAsync_ConfigFilterConfiguresCluster_Works()
        {
            var route = new ProxyRoute
            {
                RouteId = "route1",
                ClusterId = "cluster1",
                Match = new RouteMatch { Path = "/" }
            };
            var cluster = new Cluster()
            {
                Id = "cluster1",
                Destinations = new Dictionary<string, Destination>(StringComparer.OrdinalIgnoreCase)
                {
                    { "d1", new Destination() { Address = "http://localhost" } }
                }
            };
            var services = CreateServices(new List<ProxyRoute>() { route }, new List<Cluster>() { cluster }, proxyBuilder =>
            {
                proxyBuilder.AddConfigFilter<ClusterAndRouteFilter>();
            });
            var manager = services.GetRequiredService<ProxyConfigManager>();
            var dataSource = await manager.InitialLoadAsync();

            Assert.NotNull(dataSource);
            var endpoint = Assert.Single(dataSource.Endpoints);
            var routeConfig = endpoint.Metadata.GetMetadata<RouteConfig>();
            var clusterInfo = routeConfig.Cluster;
            Assert.NotNull(clusterInfo);
            Assert.True(clusterInfo.Config.Options.HealthCheck.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(12), clusterInfo.Config.Options.HealthCheck.Active.Interval);
            var destination = Assert.Single(clusterInfo.DynamicState.AllDestinations);
            Assert.Equal("http://localhost", destination.Config.Options.Address);
        }

        private class ClusterAndRouteThrows : IProxyConfigFilter
        {
            public ValueTask<Cluster> ConfigureClusterAsync(Cluster cluster, CancellationToken cancel)
            {
                throw new NotFiniteNumberException("Test exception");
            }

            public ValueTask<ProxyRoute> ConfigureRouteAsync(ProxyRoute route, CancellationToken cancel)
            {
                throw new NotFiniteNumberException("Test exception");
            }
        }

        [Fact]
        public async Task LoadAsync_ConfigFilterClusterActionThrows_Throws()
        {
            var cluster = new Cluster()
            {
                Id = "cluster1",
                Destinations = new Dictionary<string, Destination>(StringComparer.OrdinalIgnoreCase)
                {
                    { "d1", new Destination() { Address = "http://localhost" } }
                }
            };
            var services = CreateServices(new List<ProxyRoute>(), new List<Cluster>() { cluster }, proxyBuilder =>
            {
                proxyBuilder.AddConfigFilter<ClusterAndRouteThrows>();
                proxyBuilder.AddConfigFilter<ClusterAndRouteThrows>();
            });
            var configManager = services.GetRequiredService<ProxyConfigManager>();

            var ioEx = await Assert.ThrowsAsync<InvalidOperationException>(() => configManager.InitialLoadAsync());
            Assert.Equal("Unable to load or apply the proxy configuration.", ioEx.Message);
            var agex = Assert.IsType<AggregateException>(ioEx.InnerException);

            Assert.Single(agex.InnerExceptions);
            Assert.IsType<NotFiniteNumberException>(agex.InnerExceptions.First().InnerException);
        }


        [Fact]
        public async Task LoadAsync_ConfigFilterRouteActionThrows_Throws()
        {
            var route1 = new ProxyRoute { RouteId = "route1", Match = new RouteMatch { Hosts = new[] { "example.com" } }, Order = 1, ClusterId = "cluster1" };
            var route2 = new ProxyRoute { RouteId = "route2", Match = new RouteMatch { Hosts = new[] { "example2.com" } }, Order = 1, ClusterId = "cluster2" };
            var services = CreateServices(new List<ProxyRoute>() { route1, route2 }, new List<Cluster>(), proxyBuilder =>
            {
                proxyBuilder.AddConfigFilter<ClusterAndRouteThrows>();
                proxyBuilder.AddConfigFilter<ClusterAndRouteThrows>();
            });
            var configManager = services.GetRequiredService<ProxyConfigManager>();

            var ioEx = await Assert.ThrowsAsync<InvalidOperationException>(() => configManager.InitialLoadAsync());
            Assert.Equal("Unable to load or apply the proxy configuration.", ioEx.Message);
            var agex = Assert.IsType<AggregateException>(ioEx.InnerException);

            Assert.Equal(2, agex.InnerExceptions.Count);
            Assert.IsType<NotFiniteNumberException>(agex.InnerExceptions.First().InnerException);
            Assert.IsType<NotFiniteNumberException>(agex.InnerExceptions.Skip(1).First().InnerException);
        }
    }
}
