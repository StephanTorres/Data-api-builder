// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Used to generate a GraphQL schema from the provided database.
    ///
    /// This will take the provided database object model for entities and
    /// combine it with the runtime configuration to apply the auth config.
    ///
    /// It also generates the middleware resolvers used for the queries
    /// and mutations, based off the provided <c>IQueryEngine</c> and
    /// <c>IMutationEngine</c> for the runtime.
    /// </summary>
    public class GraphQLSchemaCreator
    {
        private readonly IQueryEngineFactory _queryEngineFactory;
        private readonly IMutationEngineFactory _mutationEngineFactory;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly RuntimeEntities _entities;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphQLSchemaCreator"/> class.
        /// </summary>
        /// <param name="runtimeConfigProvider">Runtime config provided for the instance.</param>
        /// <param name="queryEngineFactory">QueryEngineFactory to retreive query engine to be used by resolvers.</param>
        /// <param name="mutationEngineFactory">MutationEngineFactory to retreive mutation engine to be used by resolvers.</param>
        /// <param name="metadataProviderFactory">MetadataProviderFactory to get metadata provider used when generating the SQL-based GraphQL schema. Ignored if the runtime is Cosmos.</param>
        /// <param name="authorizationResolver">Authorization information for the runtime, to be applied to the GraphQL schema.</param>
        public GraphQLSchemaCreator(
            RuntimeConfigProvider runtimeConfigProvider,
            IQueryEngineFactory queryEngineFactory,
            IMutationEngineFactory mutationEngineFactory,
            IMetadataProviderFactory metadataProviderFactory,
            IAuthorizationResolver authorizationResolver)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

            _entities = runtimeConfig.Entities;
            _queryEngineFactory = queryEngineFactory;
            _mutationEngineFactory = mutationEngineFactory;
            _metadataProviderFactory = metadataProviderFactory;
            _authorizationResolver = authorizationResolver;
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Take the raw GraphQL objects and generate the full schema from them.
        /// At this point, we're somewhat agnostic to whether the runtime is Cosmos or SQL
        /// as we're working with GraphQL object types, regardless of where they came from.
        /// </summary>
        /// <param name="sb">Schema builder</param>
        /// <param name="root">Root document containing the GraphQL object and input types.</param>
        /// <param name="inputTypes">Reference table of the input types for query lookup.</param>
        private ISchemaBuilder Parse(
            ISchemaBuilder sb,
            DocumentNode root,
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            // Generate the Query and the Mutation Node.
            (DocumentNode queryNode, DocumentNode mutationNode) = GenerateQueryAndMutationNodes(root, inputTypes);

            return sb
                .AddDocument(root)
                .AddAuthorizeDirectiveType()
                // Add our custom directives
                .AddDirectiveType<ModelDirectiveType>()
                .AddDirectiveType<RelationshipDirectiveType>()
                .AddDirectiveType<PrimaryKeyDirectiveType>()
                .AddDirectiveType<DefaultValueDirectiveType>()
                .AddDirectiveType<AutoGeneratedDirectiveType>()
                // Add our custom scalar GraphQL types
                .AddType<OrderByType>()
                .AddType<DefaultValueType>()
                // Generate the GraphQL queries from the provided objects
                .AddDocument(queryNode)
                // Generate the GraphQL mutations from the provided objects
                .AddDocument(mutationNode)
                // Enable the OneOf directive (https://github.com/graphql/graphql-spec/pull/825) to support the DefaultValue type
                .ModifyOptions(o => o.EnableOneOf = true)
                // Add our custom middleware for GraphQL resolvers
                .Use((services, next) => new ResolverMiddleware(next, _queryEngineFactory, _mutationEngineFactory, _runtimeConfigProvider));
        }

        /// <summary>
        /// Generate the GraphQL schema query and mutation nodes from the provided database.
        /// </summary>
        /// <param name="root">Root document node which contains base entity types.</param>
        /// <param name="inputTypes">Dictionary with key being the object and value the input object type definition node for that object.</param>
        /// <returns>Query and mutation nodes.</returns>
        public (DocumentNode, DocumentNode) GenerateQueryAndMutationNodes(DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes)
        {
            Dictionary<string, DatabaseObject> entityToDbObjects = new();
            Dictionary<string, DatabaseType> entityToDatabaseType = new();

            HashSet<string> dataSourceNames = new();

            // Merge the entityToDBObjects for queryNode generation for all entities.
            foreach ((string entityName, _) in _entities)
            {
                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
                ISqlMetadataProvider metadataprovider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                if (!dataSourceNames.Contains(dataSourceName))
                {
                    entityToDbObjects = entityToDbObjects.Concat(metadataprovider.EntityToDatabaseObject).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    dataSourceNames.Add(dataSourceName);
                }

                entityToDatabaseType.TryAdd(entityName, metadataprovider.GetDatabaseType());
            }
            // Generate the GraphQL queries from the provided objects
            DocumentNode queryNode = QueryBuilder.Build(root, entityToDatabaseType, _entities, inputTypes, _authorizationResolver.EntityPermissionsMap, entityToDbObjects);

            // Generate the GraphQL mutations from the provided objects
            DocumentNode mutationNode = MutationBuilder.Build(root, entityToDatabaseType, _entities, _authorizationResolver.EntityPermissionsMap, entityToDbObjects);

            return (queryNode, mutationNode);
        }

        /// <summary>
        /// If the metastore provider is able to get the graphql schema,
        /// this function parses it and attaches resolvers to the various query fields.
        /// </summary>
        /// <exception cref="NotImplementedException">Thrown if the database type is not supported</exception>
        /// <returns>The <c>ISchemaBuilder</c> for HotChocolate, with the generated GraphQL schema</returns>
        public ISchemaBuilder InitializeSchemaAndResolvers(ISchemaBuilder schemaBuilder)
        {
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = GenerateGraphQLObjects();

            return Parse(schemaBuilder, root, inputTypes);
        }

        /// <summary>
        /// Generates the ObjectTypeDefinitionNodes and InputObjectTypeDefinitionNodes as part of GraphQL Schema generation
        /// with the provided entities listed in the runtime configuration that match the provided database type.
        /// </summary>
        /// <param name="entities">Key/Value Collection {entityName -> Entity object}</param>
        /// <returns>Root GraphQLSchema DocumentNode and inputNodes to be processed by downstream schema generation helpers.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private DocumentNode GenerateSqlGraphQLObjects(RuntimeEntities entities, Dictionary<string, InputObjectTypeDefinitionNode> inputObjects)
        {
            Dictionary<string, ObjectTypeDefinitionNode> objectTypes = new();

            // First pass - build up the object and input types for all the entities
            foreach ((string entityName, Entity entity) in entities)
            {
                // Skip creating the GraphQL object for the current entity due to configuration
                // explicitly excluding the entity from the GraphQL endpoint.
                if (!entity.GraphQL.Enabled)
                {
                    continue;
                }

                string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
                ISqlMetadataProvider sqlMetadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);

                if (sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(entityName, out DatabaseObject? databaseObject))
                {
                    // Collection of role names allowed to access entity, to be added to the authorize directive
                    // of the objectTypeDefinitionNode. The authorize Directive is one of many directives created.
                    IEnumerable<string> rolesAllowedForEntity = _authorizationResolver.GetRolesForEntity(entityName);
                    Dictionary<string, IEnumerable<string>> rolesAllowedForFields = new();
                    SourceDefinition sourceDefinition = sqlMetadataProvider.GetSourceDefinition(entityName);
                    bool isStoredProcedure = entity.Source.Type is EntitySourceType.StoredProcedure;
                    foreach (string column in sourceDefinition.Columns.Keys)
                    {
                        EntityActionOperation operation = isStoredProcedure ? EntityActionOperation.Execute : EntityActionOperation.Read;
                        IEnumerable<string> roles = _authorizationResolver.GetRolesForField(entityName, field: column, operation: operation);
                        if (!rolesAllowedForFields.TryAdd(key: column, value: roles))
                        {
                            throw new DataApiBuilderException(
                                message: "Column already processed for building ObjectTypeDefinition authorization definition.",
                                statusCode: HttpStatusCode.InternalServerError,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization
                                );
                        }
                    }

                    // The roles allowed for Fields are the roles allowed to READ the fields, so any role that has a read definition for the field.
                    // Only add objectTypeDefinition for GraphQL if it has a role definition defined for access.
                    if (rolesAllowedForEntity.Any())
                    {
                        ObjectTypeDefinitionNode node = SchemaConverter.FromDatabaseObject(
                            entityName,
                            databaseObject,
                            entity,
                            entities,
                            rolesAllowedForEntity,
                            rolesAllowedForFields
                        );

                        if (databaseObject.SourceType is not EntitySourceType.StoredProcedure)
                        {
                            InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);
                        }

                        objectTypes.Add(entityName, node);
                    }
                }
                else
                {
                    throw new DataApiBuilderException(message: $"Database Object definition for {entityName} has not been inferred.",
                        statusCode: HttpStatusCode.InternalServerError,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                }
            }

            // Pass two - Add the arguments to the many-to-* relationship fields
            foreach ((string entityName, ObjectTypeDefinitionNode node) in objectTypes)
            {
                objectTypes[entityName] = QueryBuilder.AddQueryArgumentsForRelationships(node, inputObjects);
            }

            Dictionary<string, FieldDefinitionNode> fields = new();
            NameNode nameNode = new(value: GraphQLUtils.DB_OPERATION_RESULT_TYPE);
            FieldDefinitionNode field = MutationBuilder.GetDefaultResultFieldForMutation();

            fields.TryAdd("result", field);

            objectTypes.Add(GraphQLUtils.DB_OPERATION_RESULT_TYPE, new ObjectTypeDefinitionNode(
                location: null,
                name: nameNode,
                description: null,
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                fields.Values.ToImmutableList()));

            List<IDefinitionNode> nodes = new(objectTypes.Values);
            return new DocumentNode(nodes);
        }

        /// <summary>
        /// Generates the ObjectTypeDefinitionNodes and InputObjectTypeDefinitionNodes as part of GraphQL Schema generation for cosmos db.
        /// Each datasource in cosmos has a root file provided which is used to generate the schema.
        /// NOTE: DataSourceNames must be preFiltered to be cosmos datasources.
        /// </summary>
        /// <param name="dataSourceNames">Hashset of datasourceNames to generate cosmos objects.</param>
        private DocumentNode GenerateCosmosGraphQLObjects(HashSet<string> dataSourceNames, Dictionary<string, InputObjectTypeDefinitionNode> inputObjects)
        {
            DocumentNode? root = null;

            if (dataSourceNames.Count() == 0)
            {
                return new DocumentNode(new List<IDefinitionNode>());
            }

            foreach (string dataSourceName in dataSourceNames)
            {
                ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
                DocumentNode currentNode = ((CosmosSqlMetadataProvider)metadataProvider).GraphQLSchemaRoot;
                root = root is null ? currentNode : root.WithDefinitions(root.Definitions.Concat(currentNode.Definitions).ToImmutableList());
            }

            IEnumerable<ObjectTypeDefinitionNode> objectNodes = root!.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>();
            foreach (ObjectTypeDefinitionNode node in objectNodes)
            {
                InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);
            }

            return root;
        }

        public (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateGraphQLObjects()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            HashSet<string> cosmosDataSourceNames = new();
            IDictionary<string, Entity> sqlEntities = new Dictionary<string, Entity>();
            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();

            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                DataSource ds = runtimeConfig.GetDataSourceFromEntityName(entityName);

                switch (ds.DatabaseType)
                {
                    case DatabaseType.CosmosDB_NoSQL:
                        cosmosDataSourceNames.Add(_runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName));
                        break;
                    case DatabaseType.DWSQL:

                    case DatabaseType.MSSQL or DatabaseType.MySQL or DatabaseType.PostgreSQL or DatabaseType.DWSQL:
                        sqlEntities.TryAdd(entityName, entity);
                        break;
                    default:
                        throw new NotImplementedException($"This database type {ds.DatabaseType} is not yet implemented.");
                }
            }

            RuntimeEntities sql = new(new ReadOnlyDictionary<string, Entity>(sqlEntities));

            DocumentNode cosmosResult = GenerateCosmosGraphQLObjects(cosmosDataSourceNames, inputObjects);
            DocumentNode sqlResult = GenerateSqlGraphQLObjects(sql, inputObjects);
            // Create Root node with definitions from both cosmos and sql.
            DocumentNode root = new(cosmosResult.Definitions.Concat(sqlResult.Definitions).ToImmutableList());

            // Merge the inputobjectType definitions from cosmos and sql onto the root.
            return (root.WithDefinitions(root.Definitions.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
        }
    }
}
