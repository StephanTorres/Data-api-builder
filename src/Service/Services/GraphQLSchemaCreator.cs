// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Service.Services
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
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;
        private readonly ISqlMetadataProvider _sqlMetadataProvider;
        private readonly DatabaseType _databaseType;
        private readonly Dictionary<string, Entity> _entities;
        private readonly IAuthorizationResolver _authorizationResolver;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphQLSchemaCreator"/> class.
        /// </summary>
        /// <param name="runtimeConfigProvider">Runtime config provided for the instance.</param>
        /// <param name="queryEngine">SQL or Cosmos query engine to be used by resolvers.</param>
        /// <param name="mutationEngine">SQL or Cosmos mutation engine to be used by resolvers.</param>
        /// <param name="sqlMetadataProvider">Metadata provider used when generating the SQL-based GraphQL schema. Ignored if the runtime is Cosmos.</param>
        /// <param name="authorizationResolver">Authorization information for the runtime, to be applied to the GraphQL schema.</param>
        public GraphQLSchemaCreator(
            RuntimeConfigProvider runtimeConfigProvider,
            IQueryEngine queryEngine,
            IMutationEngine mutationEngine,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();

            _databaseType = runtimeConfig.DatabaseType;
            _entities = runtimeConfig.Entities;
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
            _sqlMetadataProvider = sqlMetadataProvider;
            _authorizationResolver = authorizationResolver;
        }

        /// <summary>
        /// Take the raw GraphQL objects and generate the full schema from them.
        ///
        /// At this point, we're somewhat agnostic to whether the runtime is Cosmos or SQL
        /// as we're working with GraphQL object types, regardless of where they came from.
        /// </summary>
        /// <param name="sb">Schema builder</param>
        /// <param name="root">Root document containing the GraphQL object and input types.</param>
        /// <param name="inputTypes">Reference table of the input types for query lookup.</param>
        /// <param name="dbObjects">Database metadata such as parameters</param>
        private ISchemaBuilder Parse(
            ISchemaBuilder sb,
            DocumentNode root,
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes,
            Dictionary<string, DatabaseObject> dbObjects)
        {
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
                .AddDocument(QueryBuilder.Build(root, _databaseType, _entities, inputTypes, _authorizationResolver.EntityPermissionsMap, dbObjects))
                // Generate the GraphQL mutations from the provided objects
                .AddDocument(MutationBuilder.Build(root, _databaseType, _entities, _authorizationResolver.EntityPermissionsMap, dbObjects))
                // Enable the OneOf directive (https://github.com/graphql/graphql-spec/pull/825) to support the DefaultValue type
                .ModifyOptions(o => o.EnableOneOf = true)
                // Adds our type interceptor that will create the resolvers.
                .TryAddTypeInterceptor(new ResolverTypeInterceptor(new ExecutionHelper(_queryEngine, _mutationEngine)));
        }

        /// <summary>
        /// If the metastore provider is able to get the graphql schema,
        /// this function parses it and attaches resolvers to the various query fields.
        /// </summary>
        /// <exception cref="NotImplementedException">Thrown if the database type is not supported</exception>
        /// <returns>The <c>ISchemaBuilder</c> for HotChocolate, with the generated GraphQL schema</returns>
        public ISchemaBuilder InitializeSchemaAndResolvers(ISchemaBuilder schemaBuilder)
        {
            (DocumentNode root, Dictionary<string, InputObjectTypeDefinitionNode> inputTypes) = _databaseType switch
            {
                DatabaseType.cosmosdb_nosql => GenerateCosmosGraphQLObjects(),
                DatabaseType.mssql or
                DatabaseType.postgresql or
                DatabaseType.mysql => GenerateSqlGraphQLObjects(_entities),
                _ => throw new NotImplementedException($"This database type {_databaseType} is not yet implemented.")
            };

            return Parse(schemaBuilder, root, inputTypes, _sqlMetadataProvider.EntityToDatabaseObject);
        }

        /// <summary>
        /// Generates the ObjectTypeDefinitionNodes and InputObjectTypeDefinitionNodes as part of GraphQL Schema generation
        /// with the provided entities listed in the runtime configuration.
        /// </summary>
        /// <param name="entities">Key/Value Collection {entityName -> Entity object}</param>
        /// <returns>Root GraphQLSchema DocumentNode and inputNodes to be processed by downstream schema generation helpers.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
        private (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateSqlGraphQLObjects(Dictionary<string, Entity> entities)
        {
            Dictionary<string, ObjectTypeDefinitionNode> objectTypes = new();
            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();

            // First pass - build up the object and input types for all the entities
            foreach ((string entityName, Entity entity) in entities)
            {
                // Skip creating the GraphQL object for the current entity due to configuration
                // explicitly excluding the entity from the GraphQL endpoint.
                if (entity.GraphQL is not null && entity.GraphQL is bool graphql && graphql == false)
                {
                    continue;
                }

                if (_sqlMetadataProvider.GetEntityNamesAndDbObjects().TryGetValue(entityName, out DatabaseObject? databaseObject))
                {
                    // Collection of role names allowed to access entity, to be added to the authorize directive
                    // of the objectTypeDefinitionNode. The authorize Directive is one of many directives created.
                    IEnumerable<string> rolesAllowedForEntity = _authorizationResolver.GetRolesForEntity(entityName);
                    Dictionary<string, IEnumerable<string>> rolesAllowedForFields = new();
                    SourceDefinition sourceDefinition = _sqlMetadataProvider.GetSourceDefinition(entityName);
                    bool isStoredProcedure = entity.ObjectType is SourceType.StoredProcedure;
                    foreach (string column in sourceDefinition.Columns.Keys)
                    {
                        Config.Operation operation = isStoredProcedure ? Config.Operation.Execute : Config.Operation.Read;
                        IEnumerable<string> roles = _authorizationResolver.GetRolesForField(entityName, field: column, operation: operation);
                        if (!rolesAllowedForFields.TryAdd(key: column, value: roles))
                        {
                            throw new DataApiBuilderException(
                                message: "Column already processed for building ObjectTypeDefinition authorization definition.",
                                statusCode: System.Net.HttpStatusCode.InternalServerError,
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

                        if (databaseObject.SourceType is not SourceType.StoredProcedure)
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

            List<IDefinitionNode> nodes = new(objectTypes.Values);
            return (new DocumentNode(nodes.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
        }

        private (DocumentNode, Dictionary<string, InputObjectTypeDefinitionNode>) GenerateCosmosGraphQLObjects()
        {
            string graphqlSchema = ((CosmosSqlMetadataProvider)_sqlMetadataProvider).GraphQLSchema();

            if (string.IsNullOrEmpty(graphqlSchema))
            {
                throw new DataApiBuilderException(
                    message: "No GraphQL object model was provided for CosmosDB. Please define a GraphQL object model and link it in the runtime config.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new();
            DocumentNode root = Utf8GraphQLParser.Parse(graphqlSchema);

            IEnumerable<ObjectTypeDefinitionNode> objectNodes = root.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>();
            foreach (ObjectTypeDefinitionNode node in objectNodes)
            {
                InputTypeBuilder.GenerateInputTypesForObjectType(node, inputObjects);
            }

            return (root.WithDefinitions(root.Definitions.Concat(inputObjects.Values).ToImmutableList()), inputObjects);
        }
    }
}
