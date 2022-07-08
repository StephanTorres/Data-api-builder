using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Resolvers;

namespace Azure.DataGateway.Service.Services.MetadataProviders
{
    public class CosmosSqlMetadataProvider : ISqlMetadataProvider
    {
        private readonly IFileSystem _fileSystem;
        private readonly DatabaseType _databaseType;
        private readonly Dictionary<string, Entity> _entities;
        private CosmosDbOptions _cosmosDb;
        private readonly RuntimeConfig _runtimeConfig;
        private Dictionary<string, string> _partitionKeyPaths = new();

        public FilterParser ODataFilterParser => new();

        /// <inheritdoc />
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        public CosmosSqlMetadataProvider(RuntimeConfigProvider runtimeConfigProvider, IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
            _runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();

            _databaseType = _runtimeConfig.DatabaseType;
            _entities = _runtimeConfig.Entities;

            CosmosDbOptions? cosmosDb = _runtimeConfig.CosmosDb;

            if (cosmosDb is null)
            {
                throw new DataGatewayException(
                    message: "No CosmosDB configuration provided but CosmosDB is the specified database.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            _cosmosDb = cosmosDb;
        }

        /// <inheritdoc />
        public string GetDatabaseObjectName(string entityName)
        {
            Entity entity = _entities[entityName];

            string entitySource = entity.GetSourceName();

            return entitySource switch
            {
                string s when string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(_cosmosDb.Container) => _cosmosDb.Container,
                string s when !string.IsNullOrEmpty(s) => EntitySourceNamesParser.ParseSchemaAndTable(entitySource).Item2,
                string s => s,
                _ => throw new DataGatewayException(
                        message: $"No container provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization)
            };
        }

        /// <inheritdoc />
        public DatabaseType GetDatabaseType()
        {
            return _databaseType;
        }

        /// <inheritdoc />
        public string GetSchemaName(string entityName)
        {
            Entity entity = _entities[entityName];

            string entitySource = entity.GetSourceName();

            if (string.IsNullOrEmpty(entitySource))
            {
                return _cosmosDb.Database;
            }

            (string? database, _) = EntitySourceNamesParser.ParseSchemaAndTable(entitySource);

            return database switch
            {
                string s when string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(_cosmosDb.Container) => _cosmosDb.Database,
                string s => s,
                _ => throw new DataGatewayException(
                        message: $"No container provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization)
            };
        }

        public TableDefinition GetTableDefinition(string entityName)
        {
            throw new NotSupportedException("Cosmos backends don't support direct table definitions. Definitions are provided via the GraphQL schema");
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public string GraphQLSchema()
        {
            string? graphQLSchema = null;
            if (string.IsNullOrEmpty(_cosmosDb.GraphQLSchemaPath))
            {
                throw new DataGatewayException(
                    "No GraphQL schema file has been provided for CosmosDB.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            if (_fileSystem.File.Exists(_cosmosDb.GraphQLSchemaPath))
            {
                graphQLSchema = _fileSystem.File.ReadAllText(_cosmosDb.GraphQLSchemaPath);
            }
            else
            {
                throw new DataGatewayException(
                    "GraphQL Schema Path is Invalid.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            if (string.IsNullOrEmpty(graphQLSchema))
            {
                throw new DataGatewayException(
                    "GraphQL Schema isn't set.",
                    System.Net.HttpStatusCode.InternalServerError,
                    DataGatewayException.SubStatusCodes.ErrorInInitialization);
            }

            return graphQLSchema;
        }

        public FilterParser GetODataFilterParser()
        {
            throw new NotImplementedException();
        }

        public IQueryBuilder GetQueryBuilder()
        {
            throw new NotImplementedException();
        }

        public bool TryGetExposedColumnName(string entityName, string field, out string? name)
        {
            throw new NotImplementedException();
        }

        public bool TryGetBackingColumn(string entityName, string field, out string? name)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<KeyValuePair<string, DatabaseObject>> GetEntityNamesAndDbObjects()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public string? GetPartitionKeyPath(string database, string container)
        {
            _partitionKeyPaths.TryGetValue($"{database}/{container}", out string? partitionKeyPath);
            return partitionKeyPath;
        }

        /// <inheritdoc />
        public void SetPartitionKeyPath(string database, string container, string partitionKeyPath)
        {
            if (!_partitionKeyPaths.TryAdd($"{database}/{container}", partitionKeyPath))
            {
                _partitionKeyPaths[$"{database}/{container}"] = partitionKeyPath;
            }
        }
    }
}
