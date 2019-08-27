Clustered ASP.NET Core Microservices
====
.NEXT provides fully-featured implementation of cluster computing infrastructure for microservices constructed on top of ASP.NET Core. This implementation consists of the following features:
* Point-to-point messaging between microservices organized through HTTP
* Consensus algorithm is Raft and all necessary communication for this algorithm is based on HTTP
* Replication according with Raft algorithm is fully supported. In-memory audit trail is used by default.

In this implementation, Web application treated as cluster node. The following example demonstrates how to turn ASP.NET Core application into cluster node:
```csharp
using DotNext.Net.Cluster.Consensus.Raft.Http.Embedding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

sealed class Startup : StartupBase
{
    private readonly IConfiguration configuration;

    public Startup(IConfiguration configuration) => this.configuration = configuration;

    public override void Configure(IApplicationBuilder app)
    {
        app.UseConsensusProtocolHandler();	//informs that processing pipeline should handle Raft-specific requests
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        services.BecomeClusterMember(configuration["memberConfig"]);	//registers all necessary services required for normal cluster node operation
    }
}

```

Raft algorithm requires dedicated HTTP endpoint for internal purposes. There are two possible ways to expose necessary endpoint:
* **Hosted Mode** exposes internal endpoint at different port because dedicated [Web Host](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhost) is used
* **Embedded Mode** exposes internal endpoint at the same port as underlying web application

The necessary mode depends on your requirements and network environment.

# Dependency Injection
Web application component can request the following service from ASP.NET Core DI container:
* [ICluster](../../api/DotNext.Net.Cluster.ICluster.yml)
* [IRaftCluster](../../api/DotNext.Net.Cluster.Consensus.Raft.IRaftCluster.yml) represents Raft-specific version of `ICluster` interface
* [IMessageBus](../../api/DotNext.Net.Cluster.Messaging.IMessageBus.yml) for point-to-point messaging between nodes
* [IExpandableCluster](../../api/DotNext.Net.Cluster.ICluster.yml) for tracking changes in cluster membership
* [IReplicationCluster&lt;ILogEntry&gt;](../../api/DotNext.Net.Cluster.Replication.IReplicationCluster-1.yml) to work with audit trail used for replication. [ILogEntry](../../api/DotNext.Net.Cluster.Consensus.Raft.ILogEntry.yml) is Raft-specific representation of the record in the audit trail.

# Configuration
The application should be configured properly to work as a cluster node. The following JSON represents example of configuration:
```json
{
	"partitioning" : false,
	"lowerElectionTimeout" : 150,
	"upperElectionTimeout" : 300,
	"members" : ["http://localhost:3262", "http://localhost:3263", "http://localhost:3264"],
	"metadata" :
	{
		"key": "value"
	},
	"allowedNetworks" : ["127.0.0.0", "255.255.0.0/16", "2001:0db9::1/64"],
	"hostAddressHint" : "192.168.0.1",
	"requestJournal" :
	{
		"memoryLimit": 5,
		"expiration": "00:00:10",
		"pollingInterval" : "00:01:00"
	},
	"resourcePath" : "/cluster-consensus/raft",
	"port" : 3262
}
```

| Configuration parameter | Required | Default Value | Description |
| ---- | ---- | ---- | ---- |
| partitioning | No | false | `true` if partitioning supported. In this case, each cluster partition may have its own leader, i.e. it is possible to have more that one leader for external observer. However, single partition cannot have more than 1 leader. `false` if partitioning is not supported and only one partition with majority of nodes can have leader. Note that cluster may be totally unavailable even if there are operating members presented
| lowerElectionTimeout, upperElectionTimeout  | No | 150, 300 |  Defines range for election timeout which is picked randomly inside of it for each cluster member. If cluster node doesn't receive heartbeat from leader node during this timeout then it becomes a candidate and start a election. The recommended value for  _upperElectionTimeout_ is `2  X lowerElectionTimeout`
| members | Yes | N/A | An array of all cluster nodes. This list must include local node. DNS name cannot be used as host name in URL except `localhost`. Only IP address is allowed |
| allowedNetworks | No | Empty list which means that all networks are allowed | List of networks with other nodes which a part of single cluster. This property can be used to restrict unathorized requests to the internal endpoint responsible for handling Raft messages |
| metadata | No | empty dictionary | A set of key/value pairs to be associated with cluster node. The metadata is queriable through `IClusterMember` interface |
| openConnectionForEachRequest | No | false | `true` to create TCP connection every time for each outbound request. `false` to use HTTP KeepAlive |
| clientHandlerName | No | raftClient | The name to be passed into [IHttpMessageHandlerFactory](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.ihttpmessagehandlerfactory) to create [HttpMessageInvoker](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpmessageinvoker) used by Raft client code |
| resourcePath | No | /cluster-consensus/raft | This configuration parameter is relevant for Embedded Mode only. It defines relative path to the endpoint responsible for handling internal Raft messages |
| port | No | 32999 | This configuration is relevant for Hosted Mode only. It defines the port number that the internal endpoint handler is listening to.
| requestJournal:memoryLimit | No | 10 | The maximum amount of memory (in MB) utilized by internal buffer used to track duplicate messages |
| requestJournal:expiration | No | 00:00:10 | The eviction time of the record containing unique request identifier |
| requestJournal:pollingInterval | No | 00:01:00 | Gets the maximum time after which the buffer updates its memory statistics |
| hostAddressHint | No | N/A | Allows to specify real IP address of the host where cluster node launched. Usually it is needed when node executed inside of Docker container. If this parameter is not specified then cluster node may fail to detect itself because network interfaces inside of Docker container have different addresses in comparison with real host network interfaces. The value can be defined at container startup time, e.g. `docker container run -e "member-config:hostAddressHint=$(hostname -i)"` |

`requestJournal` configuration section is rarely used and useful for high-load scenario only.

> [!NOTE]
> Usually, real-world ASP.NET Core application hosted on `0.0.0.0`(IPv4) or `::`(IPv6). When testing locally, use explicit loopback IP instead of `localhost` as host name for all nodes in `members` section.

Choose `lowerElectionTimeout` and `upperElectionTimeout` according with the quality of your network. If values are small then you get frequent elections and migration of leader node.

## Runtime Hook
The service implementing `IRaftCluster` is registered as singleton service. The service starts receiving Raft-specific messages immediately. Therefore, you can loose some events raised by the service such as `LeaderChanged` at starting point. To avoid that, you can implement [IRaftClusterConfigurator](../../api/DotNext.Net.Cluster.Consensus.Raft.IRaftClusterConfigurator.yml) interface and register implementation as singleton.

```csharp
using DotNext.Net.Cluster.Consensus.Raft;
using System.Collections.Generic;

internal sealed class ClusterConfigurator : IRaftClusterConfigurator
{
	private static void LeaderChanged(ICluster cluster, IClusterMember leader) {}

	private static void OnCommitted(IAuditTrail<ILogEntry> sender, long startIndex, long count) {}

	void IRaftClusterConfigurator.Initialize(IRaftCluster cluster, IDictionary<string, string> metadata)
	{
		metadata["key"] = "value";
		cluster.LeaderChanged += LeaderChanged;
		cluster.AuditTrail.Committed += OnCommitted;
	}

	void IRaftClusterConfigurator.Shutdown(IRaftCluster cluster)
	{
		cluster.LeaderChanged -= LeaderChanged;
		cluster.AuditTrail.Committed -= OnCommitted;
	}
}
```

Additionally, the hook can be used to modify metadata of the local cluster member.

## HTTP Client Behavior
.NEXT uses [HttpClient](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclient) for communication between cluster nodes. In .NET Standard, the only available HTTP handler is [HttpClientHandler](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler). It has inconsistent behavior on different platforms because relies on _libcurl_. Raft implementation uses `Timeout` property of `HttpClient` to establish request timeout. It is always defined as `upperElectionTimeout` by .NEXT infrastructure. To demonstrate inconsistent behavior let's introduce three cluster nodes: _A_, _B_ and _C_. _A_ and _B_ have been started except _C_:
* On Windows the leader will not be elected even though the majority is present - 2 of 3 nodes are available. This is happening because Connection Timeout is equal to Response Timeout, which is equal to `upperElectionTimeout`.
* On Linux everything is fine because Connection Timeout less than Response Timeout

It is not possible to specify these timeouts separately for `HttpClientHandler` as well as use [SocketsHttpHandler](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler) directly in library code because this class doesn't exist in .NET Standard. However, solution exists and presented by [IHttpMessageHandlerFactory](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.ihttpmessagehandlerfactory). You can implement this interface manually and register its implementation as singleton. .NEXT tries to use this interface if it is registered as a factory of [HttpMessageInvoker](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpmessageinvoker). The following example demonstrates how to implement this interface and create platform-independent version of message invoker:

```csharp
using System;
using System.Net.Http;

internal sealed class RaftClientHandlerFactory : IHttpMessageHandlerFactory
{
	public HttpMessageHandler CreateHandler(string name) => new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(100) };
}
```

In practice, `ConnectTimeout` should be equal to `lowerElectionTimeout` configuration property. Note that `name` parameter is equal to the `clientHandlerName` configuration property when handler creation is requested by Raft implementation.


# Hosted Mode
This mode allows to create separated [Web Host](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhost) used for hosting Raft-specific stuff. As a result, Raft implementation listens on the port that differs from the port of underlying Web application. The following example demonstrates how to write _Startup_ class for hosted mode:
```csharp
using DotNext.Net.Cluster.Consensus.Raft.Http.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal sealed class Startup : StartupBase
{
	private readonly IConfiguration configuration;

	public WebApplicationSetup(IConfiguration configuration) => this.configuration = configuration;

	public override void Configure(IApplicationBuilder app)
	{

	}

	public override void ConfigureServices(IServiceCollection services)
	{
		services.BecomeClusterMember(configuration);
	}
}
```

Note that `BecomeClusterMember` declared in [DotNext.Net.Cluster.Consensus.Raft.Http.Hosting](../../api/DotNext.Net.Cluster.Consensus.Raft.Http.Hosting.yml) namespace. 

By default, .NEXT uses Kestrel web server to serve Raft requests. However, it is possible to use manually constructed [IWebHost](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhost). In this case, `port` configuration property will be ignored.

# Embedded Mode
Embedded mode shares the same [Web Host](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhost) and port with underlying Web Application. To serve Raft-specific requests the implementation uses dedicated endpoint `/cluster-consensus/raft` that can be changed through configuration parameter. The following example demonstrates how to setup embedded mode:

```csharp
using DotNext.Net.Cluster.Consensus.Raft.Http.Embedding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

sealed class Startup : StartupBase
{
    private readonly IConfiguration configuration;

    public Startup(IConfiguration configuration) => this.configuration = configuration;

    public override void Configure(IApplicationBuilder app)
    {
        app.UseConsensusProtocolHandler();	//informs that processing pipeline should handle Raft-specific requests
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        services.BecomeClusterMember(configuration["memberConfig"]);	//registers all necessary services required for normal cluster node operation
    }
}
```

Note that `BecomeClusterMember` declared in [DotNext.Net.Cluster.Consensus.Raft.Http.Embedding](../../api/DotNext.Net.Cluster.Consensus.Raft.Http.Embedding.yml) namespace.

`UseConsensusProtocolHandler` method should be called before registration of any authentication/authorization middleware.

# Redirection to Leader
Now cluster of ASP.NET Core applications can receive requests from outside. Some of these requests may be handled by leader node only. .NEXT cluster programming model provides a way to automatically redirect request to leader node if it was originally received by follower node. The redirection is organized with help of _301 Moved Permanently_ status code. Every follower node knows the actual address of the leader node. If cluster or its partition doesn't have leader then node returns _503 Service Unavailable_. 

Automatic redirection is provided by [LeaderRouter](../../api/DotNext.Net.Cluster.Consensus.Raft.Http.LeaderRouter.yml) class. You can specify endpoint that should be handled by leader node with `RedirectToLeader` method.

```csharp
using DotNext.Net.Cluster.Consensus.Raft.Http;
using DotNext.Net.Cluster.Consensus.Raft.Http.Embedding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

sealed class Startup : StartupBase
{
    private readonly IConfiguration configuration;

    public Startup(IConfiguration configuration) => this.configuration = configuration;

    public override void Configure(IApplicationBuilder app)
    {
        app.UseConsensusProtocolHandler()
			.RedirectToLeader("/endpoint1")
			.RedirectToLeader("/endpoint2");
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        services.BecomeClusterMember(configuration);
    }
}
```

This redirection can be transparent to actual client if you use reverse proxy server such as NGINX. Reverse proxy can automatically handle redirection without returning control to the client.

# Messaging
.NEXT extension for ASP.NET Core supports messaging beween nodes through HTTP out-of-the-box. However, the infrastructure don't know how to handle custom messages. Therefore, if you want to utilize this functionality then you need to implement [IMessageHandler](../../api/DotNext.Net.Cluster.Messaging.IMessageHandler.yml) interface.

Messaging inside of cluster supports redirection to the leader as well as for external client. But this mechanism implemented differently and represented by the following methods declared in [IMessageBus](../../api/DotNext.Net.Cluster.Messaging.IMessageBus.yml) interface:
* `SendMessageToLeaderAsync` to send _Request-Reply_ message to the leader
* `SendSignalToLeaderAsync` to send _One Way_ message to the leader

# Replication
Raft algorithm requires additional persistent state in order to basic audit trail. This state is represented by [IPersistentState](../../api/DotNext.Net.Cluster.Consensus.Raft.IPersistentState.yml) interface. By default, it is implemented as in-memory storage which is suitable only for applications that doesn't have replicated state. If your application has it then implement this interface manually and use reliable storage such as disk and inject this implementation through `AuditTrail` property in [IRaftCluster](../../api/DotNext.Net.Cluster.Consensus.Raft.IRaftCluster.yml) interface. This injection should be done in user-defined implementation of [IRaftClusterConfigurator](../../api/DotNext.Net.Cluster.Consensus.Raft.IRaftClusterConfigurator.yml) interface registered as a singleton service in ASP.NET Core application.

# Example
There is Raft playground represented by RaftNode application. You can find this app [here](https://github.com/sakno/dotNext/tree/develop/src/examples/RaftNode). This playground allows to test Raft consensus protocol in real world. Each instance of launched application represents cluster node. Before starting instances you need to build application. All nodes can be started using the following script:
```bash
dotnet RaftNode.dll 3262
dotnet RaftNode.dll 3263
dotnet RaftNode.dll 3264
```

Every instance should be launched in separated Terminal session. After that, you will see diagnostics messages in `stdout` about election process. Press _Ctrl+C_ in the window related to the leader node and ensure that new leader will be elected.

Optionally, you can test replication and [WriteConcern](../../api/DotNext.Net.Cluster.Replication.WriteConcern.yml). To do that, you need to created a separate folder and place empty file into. The file changes are tracked by leader node and distributed across nodes. It should be specified as the second command-line argument:
```bash
dotnet RaftNode.dll 3262 ./folder/content.txt
dotnet RaftNode.dll 3263 ./folder/content.txt
dotnet RaftNode.dll 3264 ./folder/content.txt
```
When consensus is reached, you can open the file and change its content. You will see the message in Console window owned by leader node that the content is added into replication log. A few moments later you will that the uncomitted record is added by every cluster node, then the record will be committed by the leader node. 