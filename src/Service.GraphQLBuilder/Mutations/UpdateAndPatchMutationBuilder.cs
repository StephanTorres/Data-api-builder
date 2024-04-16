// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations
{
    public static class UpdateAndPatchMutationBuilder
    {
        public const string INPUT_ARGUMENT_NAME = "item";

        /// <summary>
        /// This method is used to determine if a field is allowed to be sent from the client in a Update/Patch mutation (eg, id field is not settable during update).
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <param name="definitions">The other named types in the schema</param>
        /// <returns>true if the field is allowed, false if it is not.</returns>
        private static bool FieldAllowedOnUpdateInput(FieldDefinitionNode field,
            DatabaseType databaseType,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            EntityActionOperation operation,
            ObjectTypeDefinitionNode parentNode)
        {
            if (IsBuiltInType(field.Type))
            {
                // For patch operation, do not include ID field in the input type
                if (IsModelType(parentNode) && databaseType is DatabaseType.CosmosDB_NoSQL && operation == EntityActionOperation.Patch)
                {
                    return field.Name.Value != QueryBuilder.ID_FIELD_NAME;
                }

                return !IsAutoGeneratedField(field);
            }

            if (QueryBuilder.IsPaginationType(field.Type.NamedType()))
            {
                return false;
            }

            HotChocolate.Language.IHasName? definition = definitions.FirstOrDefault(d => d.Name.Value == field.Type.NamedType().Name.Value);

            // When updating, you don't need to provide the data for nested models, but you will for other nested types
            // For cosmos, allow updating nested objects
            if (definition is not null && definition is ObjectTypeDefinitionNode objectType && IsModelType(objectType) && databaseType is not DatabaseType.CosmosDB_NoSQL)
            {
                return false;
            }

            return true;
        }

        private static InputObjectTypeDefinitionNode GenerateUpdateInputType(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            NameNode name,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            RuntimeEntities entities,
            DatabaseType databaseType,
            EntityActionOperation operation)
        {
            NameNode inputName = GenerateInputTypeName(operation, name.Value);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            IEnumerable<InputValueDefinitionNode> inputFields =
                objectTypeDefinitionNode.Fields
                .Where(f => FieldAllowedOnUpdateInput(f, databaseType, definitions, operation, objectTypeDefinitionNode))
                .Select(f =>
                {
                    if (!IsBuiltInType(f.Type))
                    {
                        string typeName = RelationshipDirectiveType.Target(f);
                        HotChocolate.Language.IHasName def = definitions.First(d => d.Name.Value == typeName);
                        if (def is ObjectTypeDefinitionNode otdn)
                        {
                            return GetComplexInputType(inputs, definitions, f, typeName, otdn, entities, databaseType, operation);
                        }
                    }

                    return GenerateSimpleInputType(name, f, databaseType, operation);
                });

            InputObjectTypeDefinitionNode input =
                new(
                    location: null,
                    inputName,
                    new StringValueNode($"Input type for updating {name}"),
                    new List<DirectiveNode>(),
                    inputFields.ToList()
                );

            inputs.Add(input.Name, input);
            return input;
        }

        private static InputValueDefinitionNode GenerateSimpleInputType(NameNode name, FieldDefinitionNode f, DatabaseType databaseType, EntityActionOperation operation)
        {
            return new(
                location: null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {GenerateInputTypeName(operation, name.Value)}"),
                /// There is a difference between CosmosDb for NoSql and relational databases on generating required simple field types for update mutations.
                /// Cosmos is calling replace item whereas for sql is doing incremental update.
                /// That's why sql allows nullable update input fields even for non-nullable simple fields. 
                (databaseType == DatabaseType.CosmosDB_NoSQL && operation != EntityActionOperation.Patch) ? f.Type : f.Type.NullableType(),
                defaultValue: null,
                new List<DirectiveNode>()
            );
        }

        private static InputValueDefinitionNode GetComplexInputType(
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            IEnumerable<HotChocolate.Language.IHasName> definitions,
            FieldDefinitionNode f,
            string typeName,
            ObjectTypeDefinitionNode otdn,
            RuntimeEntities entities,
            DatabaseType databaseType,
            EntityActionOperation operation)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(operation, typeName);

            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateUpdateInputType(inputs, otdn, f.Type.NamedType().Name, definitions, entities, databaseType, operation);
            }
            else
            {
                node = inputs[inputTypeName];
            }

            ITypeNode type = new NamedTypeNode(node.Name);

            // For a type like [Bar!]! we have to first unpack the outer non-null
            if (f.Type.IsNonNullType())
            {
                // The innerType is the raw List, scalar or object type without null settings
                ITypeNode innerType = f.Type.InnerType();

                if (innerType.IsListType())
                {
                    type = GenerateListType(type, innerType);
                }

                // Wrap the input with non-null to match the field definition
                type = new NonNullTypeNode((INullableTypeNode)type);
            }
            else if (f.Type.IsListType())
            {
                type = GenerateListType(type, f.Type);
            }

            return new(
                location: null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {inputTypeName}"),
                type,
                defaultValue: null,
                f.Directives
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
        /// Generates a string of the form "Update{EntityName}Input" or "Patch{EntityName}Input" for the input type.
        /// </summary>
        /// <param name="typeName">Name of the entity</param>
        /// <returns>InputTypeName</returns>
        private static NameNode GenerateInputTypeName(EntityActionOperation operation, string typeName)
        {
            return new($"{operation}{typeName}Input");
        }

        /// <summary>
        /// Generate the <c>update</c> field for the GraphQL mutations for a given object type.
        /// ReturnEntityName can be different from dbEntityName in cases where user wants summary results returned (through the DBOperationResult entity)
        /// as opposed to full entity.
        /// </summary>
        /// <param name="name">Name of the GraphQL object type</param>
        /// <param name="inputs">Reference table of known GraphQL input types</param>
        /// <param name="objectTypeDefinitionNode">GraphQL object to create the update field for.</param>
        /// <param name="root">GraphQL schema root</param>
        /// <param name="entity">Runtime config information for the object type.</param>
        /// <param name="rolesAllowedForMutation">Collection of role names allowed for action, to be added to authorize directive.</param>
        /// <returns>A <c>update*ObjectName*</c> field to be added to the Mutation type.</returns>
        public static FieldDefinitionNode Build(
            NameNode name,
            Dictionary<NameNode, InputObjectTypeDefinitionNode> inputs,
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            DocumentNode root,
            RuntimeEntities entities,
            string dbEntityName,
            DatabaseType databaseType,
            string returnEntityName,
            IEnumerable<string>? rolesAllowedForMutation = null,
            EntityActionOperation operation = EntityActionOperation.Update,
            string operationNamePrefix = "update")
        {
            InputObjectTypeDefinitionNode input = GenerateUpdateInputType(
                inputs,
                objectTypeDefinitionNode,
                name,
                root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>(),
                entities,
                databaseType,
                operation);

            List<FieldDefinitionNode> idFields = FindPrimaryKeyFields(objectTypeDefinitionNode, databaseType);
            string description;
            if (idFields.Count() > 1)
            {
                description = "One of the ids of the item being updated.";
            }
            else
            {
                description = "The ID of the item being updated.";
            }

            List<InputValueDefinitionNode> inputValues = new();
            foreach (FieldDefinitionNode idField in idFields)
            {
                inputValues.Add(new InputValueDefinitionNode(
                    location: null,
                    idField.Name,
                    new StringValueNode(description),
                    new NonNullTypeNode(idField.Type.NamedType()),
                    defaultValue: null,
                    new List<DirectiveNode>()));
            }

            inputValues.Add(new InputValueDefinitionNode(
                    location: null,
                    new NameNode(INPUT_ARGUMENT_NAME),
                    new StringValueNode($"Input representing all the fields for updating {name}"),
                    new NonNullTypeNode(new NamedTypeNode(input.Name)),
                    defaultValue: null,
                    new List<DirectiveNode>()));

            // Create authorize directive denoting allowed roles
            List<DirectiveNode> fieldDefinitionNodeDirectives = new() { new(ModelDirectiveType.DirectiveName, new ArgumentNode(ModelDirectiveType.ModelNameArgument, dbEntityName)) };

            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowedForMutation,
                    out DirectiveNode? authorizeDirective))
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            string singularName = GetDefinedSingularName(name.Value, entities[dbEntityName]);
            return new(
                location: null,
                name: new NameNode($"{operationNamePrefix}{singularName}"),
                description: new StringValueNode($"Updates a {singularName}"),
                arguments: inputValues,
                type: new NamedTypeNode(returnEntityName),
                directives: fieldDefinitionNodeDirectives
            );
        }
    }
}
