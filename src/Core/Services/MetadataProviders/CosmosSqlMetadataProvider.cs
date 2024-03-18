// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using Microsoft.OData.Edm;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    public class CosmosSqlMetadataProvider : ISqlMetadataProvider
    {
        private ODataParser _oDataParser = new();

        private readonly IFileSystem _fileSystem;
        private readonly DatabaseType _databaseType;
        private CosmosDbNoSQLDataSourceOptions _cosmosDb;
        private readonly RuntimeConfig _runtimeConfig;
        private Dictionary<string, string> _partitionKeyPaths = new();

        /// <summary>
        /// This contains each entity into EDM model convention which will be used to traverse DB Policy filter using ODataParser
        /// </summary>
        public EdmModel EdmModel { get; set; } = new();

        /// <summary>
        /// This dictionary contains entity name as key (or its alias) and its path(s) in the graphQL schema as value which will be used in the generated conditions for the entity
        /// </summary>
        public Dictionary<string, List<EntityPrefix>> EntityPathsForPrefix { get; set; } = new();

        /// <inheritdoc />
        public Dictionary<string, string> GraphQLStoredProcedureExposedNameToEntityNameMap { get; set; } = new();

        /// <inheritdoc />
        public Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; } = new(StringComparer.InvariantCultureIgnoreCase);

        public Dictionary<RelationShipPair, ForeignKeyDefinition>? PairToFkDefinition => throw new NotImplementedException();

        private Dictionary<string, List<FieldDefinitionNode>> _graphQLTypeToFieldsMap = new();

        public DocumentNode GraphQLSchemaRoot { get; set; }

        public List<Exception> SqlMetadataExceptions { get; private set; } = new();

        public CosmosSqlMetadataProvider(RuntimeConfigProvider runtimeConfigProvider, IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
            _runtimeConfig = runtimeConfigProvider.GetConfig();

            _databaseType = _runtimeConfig.DataSource.DatabaseType;

            CosmosDbNoSQLDataSourceOptions? cosmosDb = _runtimeConfig.DataSource.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>();

            if (cosmosDb is null)
            {
                throw new DataApiBuilderException(
                    message: "No CosmosDB configuration provided but CosmosDB is the specified database.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            _cosmosDb = cosmosDb;
            ParseSchemaGraphQLDocument();

            if (GraphQLSchemaRoot is null)
            {
                throw new DataApiBuilderException(
                    message: "Invalid GraphQL schema was provided for CosmosDB. Please define a valid GraphQL object model in the schema file.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            ParseSchemaGraphQLFieldsForGraphQLType();
            ParseSchemaGraphQLFieldsForPrefixes();

            InitODataParser();
        }

        /// <summary>
        /// Initialize OData parser by building OData model.
        /// The parser will be used for parsing filter clause and order by clause.
        /// </summary>
        private void InitODataParser()
        {
            _oDataParser.BuildModel(GraphQLSchemaRoot);
        }

        /// <summary>
        /// Parse the schema to get the entity paths for prefixes.
        /// It will collect all the paths for each entity and its field, starting from the container model.
        /// </summary>
        private void ParseSchemaGraphQLFieldsForPrefixes()
        {
            Dictionary<string, ObjectTypeDefinitionNode> schemaDefinitions = new();
            foreach (ObjectTypeDefinitionNode typeDefinition in GraphQLSchemaRoot.Definitions)
            {
                schemaDefinitions.Add(typeDefinition.Name.Value, typeDefinition);
            }

            foreach (IDefinitionNode typeDefinition in GraphQLSchemaRoot.Definitions)
            {
                if (typeDefinition is ObjectTypeDefinitionNode node)
                {
                    if (node.Directives.Any(a => a.Name.Value == ModelDirectiveType.DirectiveName))
                    {
                        string modelName = GraphQLNaming.ObjectTypeToEntityName(node);

                        EntityPathsForPrefix.Add(modelName, new List<EntityPrefix>() { new(Path: GraphQLNaming.COSMOSDB_CONTAINER_DEFAULT_ALIAS, EntityName: modelName) });

                        ProcessSchema(node.Fields, schemaDefinitions, GraphQLNaming.COSMOSDB_CONTAINER_DEFAULT_ALIAS, null, null);
                    }
                }
            }
        }

        /// <summary>
        /// Once container is found, it will traverse the fields and inner fields to get the paths for each entity.
        /// </summary>
        /// <param name="fields"></param>
        /// <param name="schemaDocument"></param>
        /// <param name="currentPath"></param>
        /// <param name="previousPath"></param>
        /// <param name="prevField"></param>
        private void ProcessSchema(IReadOnlyList<FieldDefinitionNode> fields,
            Dictionary<string, ObjectTypeDefinitionNode> schemaDocument,
            string currentPath,
            string? previousPath = null,
            string? prevEntityName = null,
            string? prevColumnName = null)
        {
            int tableCounter = 0;

            // Traverse the fields and add them to the path
            foreach (FieldDefinitionNode field in fields)
            {
                string entityType = field.Type.NamedType().Name.Value;
                // If the entity is not in the runtime config, skip it
                if (!_runtimeConfig.Entities.ContainsKey(entityType))
                {
                    continue;
                }

                // If the field is an array type, we need to add an alias to the table because we need to a JOIN to the same table
                string? alias = null;
                bool isArrayType = field.Type is ListTypeNode;
                if (isArrayType)
                {
                    // Since, we don't have query structure here.
                    // we are going to generate alias and use this counter to generate unique alias for each table at later stage.
                    alias = $"table{tableCounter++}";
                }

                EntityPrefix prefixInfo = new(Path: currentPath,
                            EntityName: entityType,
                            ColumnName: field.Name.Value,
                            Alias: alias);

                if (EntityPathsForPrefix.ContainsKey(entityType))
                {
                    EntityPathsForPrefix[entityType].Add(prefixInfo);
                }
                else
                {
                    EntityPathsForPrefix.Add(entityType, new List<EntityPrefix>() { prefixInfo });
                }

                if (previousPath is not null)
                {
                    EntityPathsForPrefix[entityType].Add(
                        new(Path: previousPath,
                             EntityName: prevEntityName,
                             ColumnName: prevColumnName,
                             Alias: currentPath));
                }

                // If the field is an array type, we need to add an alias to the table because we need to a JOIN to the same table
                if (isArrayType)
                {
                    ProcessSchema(fields: schemaDocument[entityType].Fields,
                                  schemaDocument: schemaDocument,
                                  currentPath: $"{alias}",
                                  previousPath: currentPath,
                                  prevEntityName: entityType,
                                  prevColumnName: field.Name.Value);
                }
                else
                {
                    ProcessSchema(fields: schemaDocument[entityType].Fields,
                                  schemaDocument: schemaDocument,
                                  currentPath: $"{currentPath}.{field.Name.Value}");
                }
            }
        }

        /// <inheritdoc />
        public string GetDatabaseObjectName(string entityName)
        {
            Entity entity = _runtimeConfig.Entities[entityName];

            string entitySource = entity.Source.Object;

            return entitySource switch
            {
                string s when string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(_cosmosDb.Container) => _cosmosDb.Container,
                string s when !string.IsNullOrEmpty(s) => EntitySourceNamesParser.ParseSchemaAndTable(entitySource).Item2,
                string s => s,
                _ => throw new DataApiBuilderException(
                        message: $"No container provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization)
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
            Entity entity = _runtimeConfig.Entities[entityName];

            string entitySource = entity.Source.Object;

            if (string.IsNullOrEmpty(entitySource))
            {
                if (string.IsNullOrEmpty(_cosmosDb.Database))
                {
                    throw new DataApiBuilderException(
                        message: $"No database provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }

                return _cosmosDb.Database;
            }

            (string? database, _) = EntitySourceNamesParser.ParseSchemaAndTable(entitySource);

            return database switch
            {
                string db when string.IsNullOrEmpty(db) && !string.IsNullOrEmpty(_cosmosDb.Database) => _cosmosDb.Database,
                string db when !string.IsNullOrEmpty(db) => db,
                _ => throw new DataApiBuilderException(
                        message: $"No database provided for {entityName}",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization)
            };
        }

        /// <summary>
        /// Even though there is no source definition for underlying entity names for
        /// cosmosdb_nosql, we return back an empty source definition required for
        /// graphql filter parser.
        /// </summary>
        /// <param name="entityName"></param>
        public SourceDefinition GetSourceDefinition(string entityName)
        {
            return new SourceDefinition();
        }

        public StoredProcedureDefinition GetStoredProcedureDefinition(string entityName)
        {
            // There's a lot of unimplemented methods here, maybe need to rethink the current interface implementation
            throw new NotSupportedException("Cosmos backends (probably) don't support direct stored procedure definitions, either.");
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        private string GraphQLSchema()
        {
            if (_cosmosDb.GraphQLSchema is not null)
            {
                return _cosmosDb.GraphQLSchema;
            }

            return _fileSystem.File.ReadAllText(_cosmosDb.Schema!);
        }

        public void ParseSchemaGraphQLDocument()
        {
            string graphqlSchema = GraphQLSchema();

            if (string.IsNullOrEmpty(graphqlSchema))
            {
                throw new DataApiBuilderException(
                    message: "No GraphQL object model was provided for CosmosDB. Please define a GraphQL object model and link it in the runtime config.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            try
            {
                GraphQLSchemaRoot = Utf8GraphQLParser.Parse(graphqlSchema);
            }
            catch (Exception)
            {
                throw new DataApiBuilderException(
                    message: "Invalid GraphQL schema was provided for CosmosDB. Please define a valid GraphQL object model in the schema file.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }
        }

        private void ParseSchemaGraphQLFieldsForGraphQLType()
        {
            IEnumerable<ObjectTypeDefinitionNode> objectNodes = GraphQLSchemaRoot.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>();
            foreach (ObjectTypeDefinitionNode node in objectNodes)
            {
                string typeName = node.Name.Value;
                _graphQLTypeToFieldsMap.TryAdd(typeName, new List<FieldDefinitionNode>());
                foreach (FieldDefinitionNode field in node.Fields)
                {
                    _graphQLTypeToFieldsMap[typeName].Add(field);
                }

                string modelName = GraphQLNaming.ObjectTypeToEntityName(node);
                // If the modelName doesn't match, such as they've overridden what's in the config with the directive
                // add a mapping for the model name as well, since sometimes we lookup via modelName (which is the config name),
                // sometimes via the GraphQL type name.
                if (modelName != typeName)
                {
                    _graphQLTypeToFieldsMap.TryAdd(modelName, _graphQLTypeToFieldsMap[typeName]);
                }
            }
        }

        public List<string> GetSchemaGraphQLFieldNamesForEntityName(string entityName)
        {
            if (_graphQLTypeToFieldsMap.TryGetValue(entityName, out List<FieldDefinitionNode>? fields))
            {
                return fields is null ? new List<string>() : fields.Select(x => x.Name.Value).ToList();
            }

            // Otherwise, entity name is not found
            return new List<string>();
        }

        /// <summary>
        /// Give an entity name and its field name,
        /// this method is to first look up the GraphQL field type using the entity name,
        /// then find the field type with the entity name and its field name.
        /// </summary>
        /// <param name="entityName">entity name</param>
        /// <param name="fieldName">GraphQL field name</param>
        /// <returns></returns>
        public string? GetSchemaGraphQLFieldTypeFromFieldName(string entityName, string fieldName)
        {
            if (_graphQLTypeToFieldsMap.TryGetValue(entityName, out List<FieldDefinitionNode>? fields))
            {
                return fields?.Where(x => x.Name.Value == fieldName).FirstOrDefault()?.Type.ToString();
            }

            return null;
        }

        public FieldDefinitionNode? GetSchemaGraphQLFieldFromFieldName(string entityName, string fieldName)
        {
            if (_graphQLTypeToFieldsMap.TryGetValue(entityName, out List<FieldDefinitionNode>? fields))
            {
                return fields?.Where(x => x.Name.Value == fieldName).FirstOrDefault();
            }

            return null;
        }

        public ODataParser GetODataParser()
        {
            return _oDataParser;
        }

        public IQueryBuilder GetQueryBuilder()
        {
            throw new NotImplementedException();
        }

        public bool VerifyForeignKeyExistsInDB(
            DatabaseTable databaseTableA,
            DatabaseTable databaseTableB)
        {
            throw new NotImplementedException();
        }

        public (string, string) ParseSchemaAndDbTableName(string source)
        {
            throw new NotImplementedException();
        }

        public bool TryGetExposedColumnName(string entityName, string field, [NotNullWhen(true)] out string? name)
        {
            name = field;
            return true;
        }

        /// <summary>
        /// Mapped column are not yet supported for Cosmos.
        /// Returns the value of the field provided.
        /// </summary>
        /// <param name="entityName">Name of the entity.</param>
        /// <param name="field">Name of the database field.</param>
        /// <param name="name">Mapped name, which for CosmosDB is the value provided for field."</param>
        /// <returns>True, with out variable set as the value of the input "field" value.</returns>
        public bool TryGetBackingColumn(string entityName, string field, [NotNullWhen(true)] out string? name)
        {
            name = field;
            return true;
        }

        public IReadOnlyDictionary<string, DatabaseObject> GetEntityNamesAndDbObjects()
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

        public bool TryGetEntityNameFromPath(string entityPathName, [NotNullWhen(true)] out string? entityName)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public string GetEntityName(string graphQLType)
        {
            if (_runtimeConfig.Entities.ContainsKey(graphQLType))
            {
                return graphQLType;
            }

            // Cosmos allows you to have a different GraphQL type name than the entity name in the config
            // and we use the `model` directive to map between the two. So if the name originally provided
            // doesn't match any entity name, we try to find the entity name by looking at the GraphQL type
            // and reading the `model` directive, then call this function again with the value from the directive.
            foreach (IDefinitionNode graphQLObject in GraphQLSchemaRoot.Definitions)
            {
                if (graphQLObject is ObjectTypeDefinitionNode objectNode &&
                    GraphQLUtils.IsModelType(objectNode) &&
                    objectNode.Name.Value == graphQLType)
                {
                    string modelName = GraphQLNaming.ObjectTypeToEntityName(objectNode);

                    return GetEntityName(modelName);
                }
            }

            // Fallback to looking at the singular name of the entity.
            foreach ((string _, Entity entity) in _runtimeConfig.Entities)
            {
                if (entity.GraphQL.Singular == graphQLType)
                {
                    return graphQLType;
                }
            }

            throw new DataApiBuilderException(
               "GraphQL type doesn't match any entity name or singular type in the runtime config.",
                System.Net.HttpStatusCode.BadRequest,
                DataApiBuilderException.SubStatusCodes.BadRequest);
        }

        /// <inheritdoc />
        public string GetDefaultSchemaName()
        {
            return string.Empty;
        }

        public bool IsDevelopmentMode()
        {
            return _runtimeConfig.IsDevelopmentMode();
        }

        public bool TryGetExposedFieldToBackingFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            throw new NotImplementedException();
        }

        public bool TryGetBackingFieldToExposedFieldMap(string entityName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? mappings)
        {
            throw new NotImplementedException();
        }

        public void InitializeAsync(
            Dictionary<string, DatabaseObject> entityToDatabaseObject,
            Dictionary<string, string> GraphQLStoredProcedureExposedNameToEntityNameMap)
        {
            throw new NotImplementedException();
        }
    }
}
