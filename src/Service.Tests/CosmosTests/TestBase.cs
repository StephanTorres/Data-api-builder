// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests;

public class TestBase
{
    internal const string DATABASE_NAME = "graphqldb";
    internal const string GRAPHQL_SCHEMA = @"
type Character @model(name:""Character"") {
    id : ID,
    name : String,
    type: String,
    homePlanet: Int,
    primaryFunction: String,
    star: Star
}

type Planet @model(name:""Planet"") {
    id : ID!,
    name : String,
    character: Character,
    age : Int,
    dimension : String,
    earth: Earth,
    stars: [Star],
    moons: [Moon],
    tags: [String!]
}

type Star @model(name:""StarAlias"") {
    id : ID,
    name : String,
    tag: Tag
}

type Tag @model(name:""TagAlias"") {
    id : ID,
    name : String
}

type Moon @model(name:""Moon"") @authorize(policy: ""Crater"") {
    id : ID,
    name : String,
    details : String
}

type Earth @model(name:""Earth"") {
    id : ID,
    name : String,
    type: String @authorize(roles: [""authenticated""])
}

type Sun @model(name:""Sun"") {
    id : ID,
    name : String
}";

    private static string[] _planets = { "Earth", "Mars", "Jupiter", "Tatooine", "Endor", "Dagobah", "Hoth", "Bespin", "Spec%ial" };

    private HttpClient _client;
    internal WebApplicationFactory<Startup> _application;
    internal string _containerName = Guid.NewGuid().ToString();

    [TestInitialize]
    public void Init()
    {
        _application = SetupTestApplicationFactory();

        _client = _application.CreateClient();
    }

    protected WebApplicationFactory<Startup> SetupTestApplicationFactory()
    {
        // Read the base config from the file system
        TestHelper.SetupDatabaseEnvironment(TestCategory.COSMOSDBNOSQL);
        RuntimeConfigLoader baseLoader = TestHelper.GetRuntimeConfigLoader();
        if (!baseLoader.TryLoadKnownConfig(out RuntimeConfig baseConfig))
        {
            throw new ApplicationException("Failed to load the default CosmosDB_NoSQL config and cannot continue with tests.");
        }

        Dictionary<string, JsonElement> updatedOptions = baseConfig.DataSource.Options;
        updatedOptions["container"] = JsonDocument.Parse($"\"{_containerName}\"").RootElement;

        RuntimeConfig updatedConfig = baseConfig
            with
        {
            DataSource = baseConfig.DataSource with { Options = updatedOptions },
            Entities = new(baseConfig.Entities.ToDictionary(e => e.Key, e => e.Value with { Source = e.Value.Source with { Object = _containerName } }))
        };

        // Setup a mock file system, and use that one with the loader/provider for the config
        MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
        {
            { @"../schema.gql", new MockFileData(GRAPHQL_SCHEMA) },
            { RuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(updatedConfig.ToJson()) }
        });
        RuntimeConfigLoader loader = new(fileSystem);
        RuntimeConfigProvider provider = new(loader);

        ISqlMetadataProvider cosmosSqlMetadataProvider = new CosmosSqlMetadataProvider(provider, fileSystem);
        IAuthorizationResolver authorizationResolverCosmos = new AuthorizationResolver(provider, cosmosSqlMetadataProvider);

        return new WebApplicationFactory<Startup>()
            .WithWebHostBuilder(builder =>
            {
                _ = builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IFileSystem>(fileSystem);
                    services.AddSingleton(loader);
                    services.AddSingleton(provider);
                    services.AddSingleton(authorizationResolverCosmos);
                });
            });
    }

    [TestCleanup]
    public void CleanupAfterEachTest()
    {
        TestHelper.UnsetAllDABEnvironmentVariables();
    }

    /// <summary>
    /// Creates items on the specified container
    /// </summary>
    /// <param name="dbName">the database name</param>
    /// <param name="containerName">the container name</param>
    /// <param name="numItems">number of items to be created</param>
    internal List<string> CreateItems(string dbName, string containerName, int numItems)
    {
        List<string> idList = new();
        CosmosClient cosmosClient = _application.Services.GetService<CosmosClientProvider>().Client;
        for (int i = 0; i < numItems; i++)
        {
            string uid = Guid.NewGuid().ToString();
            idList.Add(uid);
            dynamic sourceItem = CosmosTestHelper.GetItem(uid, _planets[i % _planets.Length], i);
            cosmosClient.GetContainer(dbName, containerName)
                .CreateItemAsync(sourceItem, new PartitionKey(uid)).Wait();
        }

        return idList;
    }

    /// <summary>
    /// Executes the GraphQL request and returns the results
    /// </summary>
    /// <param name="queryName"> Name of the GraphQL query/mutation</param>
    /// <param name="query"> The GraphQL query/mutation</param>
    /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
    /// <returns></returns>
    internal Task<JsonElement> ExecuteGraphQLRequestAsync(string queryName, string query, Dictionary<string, object> variables = null, string authToken = null, string clientRoleHeader = null)
    {
        RuntimeConfigProvider configProvider = _application.Services.GetService<RuntimeConfigProvider>();
        return GraphQLRequestExecutor.PostGraphQLRequestAsync(_client, configProvider, queryName, query, variables, authToken, clientRoleHeader);
    }

    internal async Task<JsonDocument> ExecuteCosmosRequestAsync(string query, int pageSize, string continuationToken, string containerName)
    {
        QueryRequestOptions options = new()
        {
            MaxItemCount = pageSize,
        };
        CosmosClient cosmosClient = _application.Services.GetService<CosmosClientProvider>().Client;
        Container c = cosmosClient.GetContainer(DATABASE_NAME, containerName);
        QueryDefinition queryDef = new(query);
        FeedIterator<JObject> resultSetIterator = c.GetItemQueryIterator<JObject>(queryDef, continuationToken, options);
        FeedResponse<JObject> firstPage = await resultSetIterator.ReadNextAsync();
        JArray jsonArray = new();
        IEnumerator<JObject> enumerator = firstPage.GetEnumerator();
        while (enumerator.MoveNext())
        {
            JObject item = enumerator.Current;
            jsonArray.Add(item);
        }

        return JsonDocument.Parse(jsonArray.ToString().Trim());
    }
}
