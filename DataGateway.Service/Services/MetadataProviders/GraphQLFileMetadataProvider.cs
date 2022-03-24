using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Services.MetadataProviders
{
    /// <summary>
    /// Reads GraphQL Schema and resolver config
    /// from text files to make available to GraphQL service.
    /// </summary>
    public abstract class GraphQLFileMetadataProvider : IGraphQLMetadataProvider
    {
        public ResolverConfig GraphQLResolverConfig { get; set; }

        /// <summary>
        /// Stores mutation resolvers contained in configuration file.
        /// </summary>
        private Dictionary<string, MutationResolver> _mutationResolvers;

        public DatabaseType CloudDbType { get; set; }

        public GraphQLFileMetadataProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            DataGatewayConfig config = dataGatewayConfig.Value;
            if (!config.DatabaseType.HasValue)
            {
                throw new ArgumentNullException("dataGatewayConfig.DatabaseType",
                    "The database type should be set before creating a MetadataStoreProvider");
            }

            CloudDbType = config.DatabaseType.Value;

            string? resolverConfigJson = config.ResolverConfig;
            string? graphQLSchema = config.GraphQLSchema;

            if (string.IsNullOrEmpty(resolverConfigJson) && !string.IsNullOrEmpty(config.ResolverConfigFile))
            {
                resolverConfigJson = File.ReadAllText(config.ResolverConfigFile);
            }

            if (string.IsNullOrEmpty(resolverConfigJson))
            {
                throw new ArgumentNullException("dataGatewayConfig.ResolverConfig",
                    "The resolver config should be set either via ResolverConfig or ResolverConfigFile.");
            }

            GraphQLResolverConfig = GetDeserializedConfig(resolverConfigJson);

            if (string.IsNullOrEmpty(GraphQLResolverConfig.GraphQLSchema))
            {
                if (string.IsNullOrEmpty(graphQLSchema))
                {
                    graphQLSchema = File.ReadAllText(GraphQLResolverConfig.GraphQLSchemaFile ?? "schema.gql");
                }

                GraphQLResolverConfig = GraphQLResolverConfig with { GraphQLSchema = graphQLSchema };
            }

            _mutationResolvers = new();
            foreach (MutationResolver resolver in GraphQLResolverConfig.MutationResolvers)
            {
                _mutationResolvers.Add(resolver.Id, resolver);
            }
        }

        public GraphQLFileMetadataProvider(
            GraphQLFileMetadataProvider source)
        {
            GraphQLResolverConfig = source.GraphQLResolverConfig;
            _mutationResolvers = source._mutationResolvers;
            CloudDbType = source.CloudDbType;
        }

        /// Default Constructor for Mock tests.
        public GraphQLFileMetadataProvider()
        {
            GraphQLResolverConfig = new(string.Empty, string.Empty);
            _mutationResolvers = new();
            CloudDbType = DatabaseType.None;
        }

        /// <summary>
        /// Reads generated JSON configuration file with GraphQL Schema
        /// </summary>
        /// <returns>GraphQL schema as string </returns>
        public string GetGraphQLSchema()
        {
            return GraphQLResolverConfig.GraphQLSchema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            if (!_mutationResolvers.TryGetValue(name, out MutationResolver? resolver))
            {
                throw new KeyNotFoundException("Mutation Resolver does not exist.");
            }

            return resolver;
        }

        public GraphQLType GetGraphQLType(string name)
        {
            if (!GraphQLResolverConfig.GraphQLTypes.TryGetValue(name, out GraphQLType? typeInfo))
            {
                throw new KeyNotFoundException($"Table Definition for {name} does not exist.");
            }

            return typeInfo;
        }

        public ResolverConfig GetResolvedConfig()
        {
            return GraphQLResolverConfig;
        }

        public static ResolverConfig GetDeserializedConfig(string resolverConfigJson)
        {
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());

            // This feels verbose but it avoids having to make _config nullable - which would result in more
            // down the line issues and null check requirements
            ResolverConfig? deserializedConfig;
            if ((deserializedConfig = JsonSerializer.Deserialize<ResolverConfig>(resolverConfigJson, options)) == null)
            {
                throw new JsonException("Failed to get a ResolverConfig from the provided config");
            }

            return deserializedConfig!;
        }
    }
}
