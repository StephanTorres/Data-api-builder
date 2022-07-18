using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataGateway.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    public static class UpdateMutationBuilder
    {
        public const string INPUT_ARGUMENT_NAME = "item";

        /// <summary>
        /// This method is used to determine if a field is allowed to be sent from the client in a Update mutation (eg, id field is not settable during update).
        /// </summary>
        /// <param name="field">Field to check</param>
        /// <param name="definitions">The other named types in the schema</param>
        /// <returns>true if the field is allowed, false if it is not.</returns>
        private static bool FieldAllowedOnUpdateInput(FieldDefinitionNode field, DatabaseType databaseType, IEnumerable<HotChocolate.Language.IHasName> definitions)
        {
            if (IsBuiltInType(field.Type))
            {
                return !IsAutoGeneratedField(field);
            }

            if (QueryBuilder.IsPaginationType(field.Type.NamedType()))
            {
                return false;
            }

            HotChocolate.Language.IHasName? definition = definitions.FirstOrDefault(d => d.Name.Value == field.Type.NamedType().Name.Value);
            // When updating, you don't need to provide the data for nested models, but you will for other nested types
            // For cosmos, allow updating nested objects
            if (definition is not null && definition is ObjectTypeDefinitionNode objectType && IsModelType(objectType) && databaseType is not DatabaseType.cosmos)
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
            Entity entity,
            DatabaseType databaseType)
        {
            NameNode inputName = GenerateInputTypeName(name.Value, entity);

            if (inputs.ContainsKey(inputName))
            {
                return inputs[inputName];
            }

            IEnumerable<InputValueDefinitionNode> inputFields =
                objectTypeDefinitionNode.Fields
                .Where(f => FieldAllowedOnUpdateInput(f, databaseType, definitions))
                .Select(f =>
                {
                    if (!IsBuiltInType(f.Type))
                    {
                        string typeName = RelationshipDirectiveType.Target(f);
                        HotChocolate.Language.IHasName def = definitions.First(d => d.Name.Value == typeName);
                        if (def is ObjectTypeDefinitionNode otdn)
                        {
                            return GetComplexInputType(inputs, definitions, f, typeName, otdn, entity, databaseType);
                        }
                    }

                    return GenerateSimpleInputType(name, f, entity, databaseType);
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

        private static InputValueDefinitionNode GenerateSimpleInputType(NameNode name, FieldDefinitionNode f, Entity entity, DatabaseType databaseType)
        {
            return new(
                location: null,
                f.Name,
                new StringValueNode($"Input for field {f.Name} on type {GenerateInputTypeName(name.Value, entity)}"),
                /// There is a different between Cosmos and relational databases on generating required simple field types for update mutations.
                /// Cosmos is calling replace item whereas for sql is doing incremental update.
                /// That's why sql allows nullable update input fields even for non-nullable simple fields. 
                (databaseType == DatabaseType.cosmos) ? f.Type : f.Type.NullableType(),
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
            Entity entity,
            DatabaseType databaseType)
        {
            InputObjectTypeDefinitionNode node;
            NameNode inputTypeName = GenerateInputTypeName(typeName, entity);
            if (!inputs.ContainsKey(inputTypeName))
            {
                node = GenerateUpdateInputType(inputs, otdn, f.Type.NamedType().Name, definitions, entity, databaseType);
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

        private static NameNode GenerateInputTypeName(string typeName, Entity entity)
        {
            return new($"{Operation.Update}{FormatNameForObject(typeName, entity)}Input");
        }

        /// <summary>
        /// Generate the <c>update</c> field for the GraphQL mutations for a given object type.
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
            Entity entity,
            DatabaseType databaseType,
            IEnumerable<string>? rolesAllowedForMutation = null)
        {
            InputObjectTypeDefinitionNode input = GenerateUpdateInputType(inputs, objectTypeDefinitionNode, name, root.Definitions.Where(d => d is HotChocolate.Language.IHasName).Cast<HotChocolate.Language.IHasName>(), entity, databaseType);
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
            List<DirectiveNode> fieldDefinitionNodeDirectives = new();

            // Any roles passed in will be added to the authorize directive for this field
            // taking the form: @authorize(roles: [“role1”, ..., “roleN”])
            // If the 'anonymous' role is present in the role list, no @authorize directive will be added
            // because HotChocolate requires an authenticated user when the authorize directive is evaluated.
            if (rolesAllowedForMutation is not null &&
                rolesAllowedForMutation.Count() >= 1 &&
                !rolesAllowedForMutation.Contains(SYSTEM_ROLE_ANONYMOUS))
            {
                fieldDefinitionNodeDirectives.Add(CreateAuthorizationDirective(rolesAllowedForMutation));
            }

            return new(
                location: null,
                new NameNode($"update{FormatNameForObject(name, entity)}"),
                new StringValueNode($"Updates a {name}"),
                inputValues,
                new NamedTypeNode(FormatNameForObject(name, entity)),
                fieldDefinitionNodeDirectives
            );
        }
    }
}
