// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations
{
    public static class CreateMutationBuilder
    {
        private const string CREATE_MULTIPLE_MUTATION_SUFFIX = "Multiple";
        public const string INPUT_ARGUMENT_NAME = "item";
        public const string CREATE_MUTATION_PREFIX = "create";

        /// <summary>
        /// Generate the GraphQL input type from an object type for relational database.
        /// </summary>
        /// <param name="inputs">Reference table of all known input types.</param>
        /// <param name="objectTypeDefinitionNode">GraphQL object to generate the input type for.</param>
        /// <param name="name">Name of the GraphQL object type.</param>
        /// <param name="baseEntityName">In case when we are creating input type for linking object, baseEntityName is equal to the targetEntityName,
        /// else baseEntityName is equal to the name parameter.</param>
        /// <param name="definitions">All named GraphQL items in the schema (objects, enums, scalars, etc.)</param>
        /// <param name="databaseType">Database type of the relational database to generate input type for.</param>
        /// <param name="entities">Runtime config information.</param>
        /// <param name="IsMultipleCreateOperationEnabled">Indicates whether multiple create operation is enabled</param>
        /// <returns>A GraphQL input type with all expected fields mapped as GraphQL inputs.</returns>
        private static InputObjectTypeDefinitionNode GenerateCreateInputTypeForRelationalDb(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            string entityName,
            NameNode name,
            NameNode baseEntityName,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            DatabaseType databaseType,
            RuntimeEntities entities,
            bool IsMultipleCreateOperationEnabled)
        {
            NameNode inputName = GenerateInputTypeName(name.Value);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            // The input fields for a create object will be a combination of:
            // 1. Scalar input fields corresponding to columns which belong to the table.
            // 2. Complex input fields corresponding to related (target) entities (table backed entities, for now)
            // which are defined in the runtime config.
            List<InputValueDefinitionNode> inputFields = new();

            // 1. Scalar input fields.
            IEnumerable<InputValueDefinitionNode> scalarInputFields = objectTypeDefinitionNode.Fields
                .Where(field => IsBuiltInType(field.Type) && !IsAutoGeneratedField(field))
                .Select(field =>
                {
                    return GenerateScalarInputType(name, field, IsMultipleCreateOperationEnabled);
                });

            // Add scalar input fields to list of input fields for current input type.
            inputFields.AddRange(scalarInputFields);

            // Create input object for this entity.
            InputObjectTypeDefinitionNode input =
                new(
                    location: null,
                    inputName,
                    new StringValueNode($"Input type for creating {name}"),
                    new List<DirectiveNode>(),
                    inputFields
                );

            // Add input object to the dictionary of entities for which input object has already been created.
            // This input object currently holds only scalar fields.
            // The complex fields (for related entities) would be added later when we return from recursion.
            // Adding the input object to the dictionary ensures that we don't go into infinite recursion and return whenever
            // we find that the input object has already been created for the entity.
            inputs.Add(input.Name, input);

            // Generate fields for related entities when
            // 1. Multiple mutation operations are supported for the database type.
            // 2. Multiple mutation operations are enabled.
            if (IsMultipleCreateOperationEnabled)
            {
                // 2. Complex input fields.
                // Evaluate input objects for related entities.
                IEnumerable<InputValueDefinitionNode> complexInputFields =
                    objectTypeDefinitionNode.Fields
                    .Where(field => !IsBuiltInType(field.Type) && IsComplexFieldAllowedForCreateInputInRelationalDb(field, definitions))
                    .Select(field =>
                    {
                        string typeName = RelationshipDirectiveType.Target(field);
                        HotChocolate.Language.IHasName? def = definitions.FirstOrDefault(d => d.Name.Value.Equals(typeName));

                        if (def is null)
                        {
                            throw new DataApiBuilderException(
                                message: $"The type {typeName} is not a known GraphQL type, and cannot be used in this schema.",
                                statusCode: HttpStatusCode.ServiceUnavailable,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                        }

                        if (!entities.TryGetValue(entityName, out Entity? entity) || entity.Relationships is null)
                        {
                            throw new DataApiBuilderException(
                                message: $"Could not find entity metadata for entity: {entityName}.",
                                statusCode: HttpStatusCode.ServiceUnavailable,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                        }

                        string targetEntityName = entity.Relationships[field.Name.Value].TargetEntity;
                        if (IsMToNRelationship(entity, field.Name.Value))
                        {
                            // The field can represent a related entity with M:N relationship with the parent.
                            NameNode baseObjectTypeNameForField = new(typeName);
                            typeName = GenerateLinkingNodeName(baseEntityName.Value, typeName);
                            def = (ObjectTypeDefinitionNode)definitions.FirstOrDefault(d => d.Name.Value.Equals(typeName))!;

                            // Get entity definition for this ObjectTypeDefinitionNode.
                            // Recurse for evaluating input objects for related entities.
                            return GenerateComplexInputTypeForRelationalDb(
                                entityName: targetEntityName,
                                inputs: inputs,
                                definitions: definitions,
                                field: field,
                                typeName: typeName,
                                targetObjectTypeName: baseObjectTypeNameForField,
                                objectTypeDefinitionNode: (ObjectTypeDefinitionNode)def,
                                databaseType: databaseType,
                                entities: entities,
                                IsMultipleCreateOperationEnabled: IsMultipleCreateOperationEnabled);
                        }

                        // Get entity definition for this ObjectTypeDefinitionNode.
                        // Recurse for evaluating input objects for related entities.
                        return GenerateComplexInputTypeForRelationalDb(
                            entityName: targetEntityName,
                            inputs: inputs,
                            definitions: definitions,
                            field: field,
                            typeName: typeName,
                            targetObjectTypeName: new(typeName),
                            objectTypeDefinitionNode: (ObjectTypeDefinitionNode)def,
                            databaseType: databaseType,
                            entities: entities,
                            IsMultipleCreateOperationEnabled: IsMultipleCreateOperationEnabled);
                    });
                // Append relationship fields to the input fields.
                inputFields.AddRange(complexInputFields);
            }

            return input;
        }

        /// <summary>
        /// Generate the GraphQL input type from an object type for non-relational database.
        /// </summary>
        /// <param name="inputs">Reference table of all known input types.</param>
        /// <param name="objectTypeDefinitionNode">GraphQL object to generate the input type for.</param>
        /// <param name="name">Name of the GraphQL object type.</param>
        /// <param name="baseEntityName">In case when we are creating input type for linking object, baseEntityName is equal to the targetEntityName,
        /// else baseEntityName is equal to the name parameter.</param>
        /// <param name="definitions">All named GraphQL items in the schema (objects, enums, scalars, etc.)</param>
        /// <param name="databaseType">Database type of the non-relational database to generate input type for.</param>
        /// <param name="entities">Runtime config information.</param>
        /// <returns>A GraphQL input type with all expected fields mapped as GraphQL inputs.</returns>
        private static InputObjectTypeDefinitionNode GenerateCreateInputTypeForNonRelationalDb(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            NameNode name,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            DatabaseType databaseType)
        {
            NameNode inputName = GenerateInputTypeName(name.Value);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            IEnumerable<InputValueDefinitionNode> inputFields =
                objectTypeDefinitionNode.Fields
                .Select(field =>
                {
                    if (IsBuiltInType(field.Type))
                    {
                        return GenerateScalarInputType(name, field);
                    }

                    string typeName = RelationshipDirectiveType.Target(field);
                    HotChocolate.Language.IHasName? def = definitions.FirstOrDefault(d => d.Name.Value.Equals(typeName));

                    if (def is null)
                    {
                        throw new DataApiBuilderException(
                            message: $"The type {typeName} is not a known GraphQL type, and cannot be used in this schema.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                    }

                    //Get entity definition for this ObjectTypeDefinitionNode
                    return GenerateComplexInputTypeForNonRelationalDb(
                        inputs: inputs,
                        definitions: definitions,
                        field: field,
                        typeName: typeName,
                        objectTypeDefinitionNode: (ObjectTypeDefinitionNode)def,
                        databaseType: databaseType);
                });

            // Create input object for this entity.
            InputObjectTypeDefinitionNode input =
                new(
                    location: null,
                    inputName,
                    new StringValueNode($"Input type for creating {name}"),
                    new List<DirectiveNode>(),
                    inputFields.ToList()
                );

            inputs.Add(input.Name, input);
            return input;
        }

        /// <summary>
        /// This method is used to determine if a relationship field is allowed to be sent from the client in a Create mutation.
        /// If the field is a pagination field (for *:N relationships) or if we infer an object
        /// definition for the field (for *:1 relationships), the field is allowed in the create input.
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <param name="definitions">The other named types in the schema</param>
        /// <returns>true if the field is allowed, false if it is not.</returns>
        private static bool IsComplexFieldAllowedForCreateInputInRelationalDb(FieldDefinitionNode field, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            if (QueryBuilder.IsPaginationType(field.Type.NamedType()))
            {
                return true;
            }

            HotChocolate.Language.IHasName? definition = definitions.FirstOrDefault(d => d.Name.Value == field.Type.NamedType().Name.Value);
            if (definition != null && definition is ObjectTypeDefinitionNode objectType && IsModelType(objectType))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Helper method to check if a field in an entity(table) is a referencing field to a referenced field
        /// in another entity.
        /// </summary>
        /// <param name="field">Field definition.</param>
        private static bool DoesFieldHaveReferencingFieldDirective(FieldDefinitionNode field)
        {
            return field.Directives.Any(d => d.Name.Value.Equals(ReferencingFieldDirectiveType.DirectiveName));
        }

        /// <summary>
        /// Helper method to create input type for a scalar/column field in an entity.
        /// </summary>
        /// <param name="name">Name of the field.</param>
        /// <param name="fieldDefinition">Field definition.</param>
        /// <param name="databaseType">Database type</param>
        /// <param name="IsMultipleCreateOperationEnabled">Indicates whether multiple create operation is enabled</param>
        private static InputValueDefinitionNode GenerateScalarInputType(NameNode name, FieldDefinitionNode fieldDefinition, bool isMultipleCreateOperationEnabled = false)
        {
            IValueNode? defaultValue = null;

            if (DefaultValueDirectiveType.TryGetDefaultValue(fieldDefinition, out ObjectValueNode? value))
            {
                defaultValue = value.Fields[0].Value;
            }

            bool isFieldNullable = defaultValue is not null;

            if (isMultipleCreateOperationEnabled &&
                DoesFieldHaveReferencingFieldDirective(fieldDefinition))
            {
                isFieldNullable = true;
            }

            return new(
                location: null,
                fieldDefinition.Name,
                new StringValueNode($"Input for field {fieldDefinition.Name} on type {GenerateInputTypeName(name.Value)}"),
                isFieldNullable ? fieldDefinition.Type.NullableType() : fieldDefinition.Type,
                defaultValue,
                new List<DirectiveNode>()
            );
        }

        /// <summary>
        /// Generates a GraphQL Input Type value for:
        /// 1. An object type sourced from the relational database (for entities exposed in config),
        /// 2. For source->target linking object types needed to support multiple create.
        /// </summary>
        /// <param name="inputs">Dictionary of all input types, allowing reuse where possible.</param>
        /// <param name="definitions">All named GraphQL types from the schema (objects, enums, etc.) for referencing.</param>
        /// <param name="field">Field that the input type is being generated for.</param>
        /// <param name="typeName">In case of relationships with M:N cardinality, typeName = type name of linking object, else typeName = type name of target entity.</param>
        /// <param name="targetObjectTypeName">Object type name of the target entity.</param>
        /// <param name="objectTypeDefinitionNode">The GraphQL object type to create the input type for.</param>
        /// <param name="databaseType">Database type to generate the input type for.</param>
        /// <param name="entities">Runtime configuration information for entities.</param>
        /// <returns>A GraphQL input type value.</returns>
        private static InputValueDefinitionNode GenerateComplexInputTypeForRelationalDb(
            string entityName,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            FieldDefinitionNode field,
            string typeName,
            NameNode targetObjectTypeName,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DatabaseType databaseType,
            RuntimeEntities entities,
            bool IsMultipleCreateOperationEnabled)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateCreateInputTypeForRelationalDb(
                    inputs,
                    objectTypeDefinitionNode,
                    entityName,
                    new NameNode(typeName),
                    targetObjectTypeName,
                    definitions,
                    databaseType,
                    entities,
                    IsMultipleCreateOperationEnabled);
            }
            else
            {
                node = inputs[inputTypeName];
            }

            return GetComplexInputType(field, node, inputTypeName, IsMultipleCreateOperationEnabled);
        }

        /// <summary>
        /// Generates a GraphQL Input Type value for an object type, provided from the non-relational database.
        /// </summary>
        /// <param name="inputs">Dictionary of all input types, allowing reuse where possible.</param>
        /// <param name="definitions">All named GraphQL types from the schema (objects, enums, etc.) for referencing.</param>
        /// <param name="field">Field that the input type is being generated for.</param>
        /// <param name="typeName">Type name of the related entity.</param>
        /// <param name="objectTypeDefinitionNode">The GraphQL object type to create the input type for.</param>
        /// <param name="databaseType">Database type to generate the input type for.</param>
        /// <returns>A GraphQL input type value.</returns>
        private static InputValueDefinitionNode GenerateComplexInputTypeForNonRelationalDb(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            FieldDefinitionNode field,
            string typeName,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DatabaseType databaseType)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateCreateInputTypeForNonRelationalDb(
                    inputs: inputs,
                    objectTypeDefinitionNode: objectTypeDefinitionNode,
                    name: field.Type.NamedType().Name,
                    definitions: definitions,
                    databaseType: databaseType);
            }
            else
            {
                node = inputs[inputTypeName];
            }

            // For non-relational databases, multiple create operation is not supported. Hence, IsMultipleCreateOperationEnabled parameter is set to false.
            return GetComplexInputType(field, node, inputTypeName, IsMultipleCreateOperationEnabled: false);
        }

        /// <summary>
        /// Creates and returns InputValueDefinitionNode for a field representing a related entity in it's
        /// parent's InputObjectTypeDefinitionNode.
        /// </summary>
        /// <param name="relatedFieldDefinition">Related field's definition.</param>
        /// <param name="databaseType">Database type.</param>
        /// <param name="relatedFieldInputObjectTypeDefinition">Related field's InputObjectTypeDefinitionNode.</param>
        /// <param name="parentInputTypeName">Input type name of the parent entity.</param>
        /// <param name="IsMultipleCreateOperationEnabled">Indicates whether multiple create operation is supported by the database type and is enabled through config file</param>
        /// <returns></returns>
        private static InputValueDefinitionNode GetComplexInputType(
            FieldDefinitionNode relatedFieldDefinition,
            InputObjectTypeDefinitionNode relatedFieldInputObjectTypeDefinition,
            NameNode parentInputTypeName,
            bool IsMultipleCreateOperationEnabled)
        {
            ITypeNode type = new NamedTypeNode(relatedFieldInputObjectTypeDefinition.Name);
            if (IsMultipleCreateOperationEnabled)
            {
                if (RelationshipDirectiveType.Cardinality(relatedFieldDefinition) is Cardinality.Many)
                {
                    // For *:N relationships, we need to create a list type.
                    type = GenerateListType(type, relatedFieldDefinition.Type.InnerType());
                }

                // Since providing input for a relationship field is optional, the type should be nullable.
                type = (INullableTypeNode)type;
            }
            // For a type like [Bar!]! we have to first unpack the outer non-null
            else if (relatedFieldDefinition.Type.IsNonNullType())
            {
                // The innerType is the raw List, scalar or object type without null settings
                ITypeNode innerType = relatedFieldDefinition.Type.InnerType();

                if (innerType.IsListType())
                {
                    type = GenerateListType(type, innerType);
                }

                // Wrap the input with non-null to match the field definition
                type = new NonNullTypeNode((INullableTypeNode)type);
            }
            else if (relatedFieldDefinition.Type.IsListType())
            {
                type = GenerateListType(type, relatedFieldDefinition.Type);
            }

            return new(
                location: null,
                name: relatedFieldDefinition.Name,
                description: new StringValueNode($"Input for field {relatedFieldDefinition.Name} on type {parentInputTypeName}"),
                type: type,
                defaultValue: null,
                directives: relatedFieldDefinition.Directives
            );
        }

        private static ITypeNode GenerateListType(ITypeNode type, ITypeNode fieldType)
        {
            // Look at the inner type of the list type, eg: [Bar]'s inner type is Bar
            // and if it's nullable, make the input also nullable
            return fieldType.InnerType().IsNonNullType()
                ? new ListTypeNode(new NonNullTypeNode((INullableTypeNode)type))
                : new ListTypeNode(type);
        }

        /// <summary>
        /// Generates a string of the form "Create{EntityName}Input"
        /// </summary>
        /// <param name="typeName">Name of the entity</param>
        /// <returns>InputTypeName</returns>
        public static NameNode GenerateInputTypeName(string typeName)
        {
            return new($"{EntityActionOperation.Create}{typeName}Input");
        }

        /// <summary>
        /// Generate the `create` point/multiple mutation fields for the GraphQL mutations for a given Object Definition
        /// ReturnEntityName can be different from dbEntityName in cases where user wants summary results returned (through the DBOperationResult entity)
        /// as opposed to full entity.
        /// </summary>
        /// <param name="name">Name of the GraphQL object to generate the create field for.</param>
        /// <param name="inputs">All known GraphQL input types.</param>
        /// <param name="objectTypeDefinitionNode">The GraphQL object type to generate for.</param>
        /// <param name="root">The GraphQL document root to find GraphQL schema items in.</param>
        /// <param name="databaseType">Type of database we're generating the field for.</param>
        /// <param name="entities">Runtime entities specification from config.</param>
        /// <param name="dbEntityName">Entity name specified in the runtime config.</param>
        /// <param name="returnEntityName">Name of type to be returned by the mutation.</param>
        /// <param name="rolesAllowedForMutation">Collection of role names allowed for action, to be added to authorize directive.</param>
        /// <param name="IsMultipleCreateOperationEnabled">Indicates whether multiple create operation is enabled</param>
        /// <returns>A GraphQL field definition named <c>create*EntityName*</c> to be attached to the Mutations type in the GraphQL schema.</returns>
        public static IEnumerable<FieldDefinitionNode> Build(
            NameNode name,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DocumentNode root,
            DatabaseType databaseType,
            RuntimeEntities entities,
            string dbEntityName,
            string returnEntityName,
            IEnumerable<string>? rolesAllowedForMutation = null,
            bool IsMultipleCreateOperationEnabled = false)
        {
            List<FieldDefinitionNode> createMutationNodes = new();
            Entity entity = entities[dbEntityName];
            InputObjectTypeDefinitionNode input;
            if (!IsRelationalDb(databaseType))
            {
                input = GenerateCreateInputTypeForNonRelationalDb(
                    inputs,
                    objectTypeDefinitionNode,
                    name,
                    root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>(),
                    databaseType);
            }
            else
            {
                input = GenerateCreateInputTypeForRelationalDb(
                    inputs: inputs,
                    objectTypeDefinitionNode: objectTypeDefinitionNode,
                    entityName: dbEntityName,
                    name: name,
                    baseEntityName: name,
                    definitions: root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>(),
                    databaseType: databaseType,
                    entities: entities,
                    IsMultipleCreateOperationEnabled: IsMultipleCreateOperationEnabled);
            }

            List<DirectiveNode> fieldDefinitionNodeDirectives = new() { new(ModelDirectiveType.DirectiveName, new ArgumentNode(ModelDirectiveType.ModelNameArgument, dbEntityName)) };

            // Create authorize directive denoting allowed roles
            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowedForMutation,
                    out DirectiveNode? authorizeDirective))
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            string singularName = GetDefinedSingularName(name.Value, entity);

            // Create one node.
            FieldDefinitionNode createOneNode = new(
                location: null,
                name: new NameNode(GetPointCreateMutationNodeName(name.Value, entity)),
                description: new StringValueNode($"Creates a new {singularName}"),
                arguments: new List<InputValueDefinitionNode> {
                        new(
                            location : null,
                        new NameNode(MutationBuilder.ITEM_INPUT_ARGUMENT_NAME),
                        new StringValueNode($"Input representing all the fields for creating {name}"),
                        new NonNullTypeNode(new NamedTypeNode(input.Name)),
                        defaultValue: null,
                        new List<DirectiveNode>())
                },
                type: new NamedTypeNode(returnEntityName),
                directives: fieldDefinitionNodeDirectives
            );

            createMutationNodes.Add(createOneNode);

            // Multiple create node is created in the schema only when multiple create operation is enabled.
            if (IsMultipleCreateOperationEnabled)
            {
                // Create multiple node.
                FieldDefinitionNode createMultipleNode = new(
                    location: null,
                    name: new NameNode(GetMultipleCreateMutationNodeName(name.Value, entity)),
                    description: new StringValueNode($"Creates multiple new {GetDefinedPluralName(name.Value, entity)}"),
                    arguments: new List<InputValueDefinitionNode> {
                        new(
                            location : null,
                            new NameNode(MutationBuilder.ARRAY_INPUT_ARGUMENT_NAME),
                            new StringValueNode($"Input representing all the fields for creating {name}"),
                            new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(input.Name))),
                            defaultValue: null,
                            new List<DirectiveNode>())
                    },
                    type: new NamedTypeNode(QueryBuilder.GeneratePaginationTypeName(GetDefinedSingularName(dbEntityName, entity))),
                    directives: fieldDefinitionNodeDirectives
                );
                createMutationNodes.Add(createMultipleNode);
            }

            return createMutationNodes;
        }

        /// <summary>
        /// Helper method to determine the name of the create one (or point create) mutation.
        /// </summary>
        public static string GetPointCreateMutationNodeName(string entityName, Entity entity)
        {
            string singularName = GetDefinedSingularName(entityName, entity);
            return $"{CREATE_MUTATION_PREFIX}{singularName}";
        }

        /// <summary>
        /// Helper method to determine the name of the create multiple mutation.
        /// If the singular and plural graphql names for the entity match, we suffix the name with 'Multiple' suffix to indicate
        /// that the mutation field is created to support insertion of multiple records in the top level entity.
        /// However if the plural and singular names are different, we use the plural name to construct the mutation.
        /// </summary>
        public static string GetMultipleCreateMutationNodeName(string entityName, Entity entity)
        {
            string singularName = GetDefinedSingularName(entityName, entity);
            string pluralName = GetDefinedPluralName(entityName, entity);
            string mutationName = singularName.Equals(pluralName) ? $"{singularName}{CREATE_MULTIPLE_MUTATION_SUFFIX}" : pluralName;
            return $"{CREATE_MUTATION_PREFIX}{mutationName}";
        }
    }
}
