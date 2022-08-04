using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder.Helpers
{
    public static class GraphQLTestHelpers
    {
        public const string BOOKGQL =
                    @"
                    type Book @model {
                        book_id: Int! @primaryKey
                    }
                    ";


        public const string BOOKSGQL =
                    @"
                    type Books @model {
                        book_id: Int! @primaryKey
                    }
                    ";

        public const string PERSONGQL =
                    @"
                    type Person @model {
                        person_id: Int! @primaryKey
                    }
                    ";

        public const string PEOPLEGQL =
                    @"
                    type People @model {
                        people_id: Int! @primaryKey
                    }
                    ";

        /// <summary>
        /// Mock the entityPermissionsMap which resolves which roles need to be included
        /// in an authorize directive used on a GraphQL object type definition.
        /// </summary>
        /// <param name="entityName">Entity for which authorization permissions need to be resolved.</param>
        /// <param name="actions">Actions performed on entity to resolve authorization permissions.</param>
        /// <param name="roles">Collection of role names allowed to perform action on entity.</param>
        /// <returns>EntityPermissionsMap Key/Value collection.</returns>
        public static Dictionary<string, EntityMetadata> CreateStubEntityPermissionsMap(string[] entityNames, IEnumerable<Operation> actions, IEnumerable<string> roles)
        {
            EntityMetadata entityMetadata = new()
            {
                ActionToRolesMap = new Dictionary<Operation, List<string>>()
            };

            foreach (Operation action in actions)
            {
                entityMetadata.ActionToRolesMap.Add(action, roles.ToList());
            }

            Dictionary<string, EntityMetadata> entityPermissionsMap = new();

            foreach (string entityName in entityNames)
            {
                entityPermissionsMap.Add(entityName, entityMetadata);
            }

            return entityPermissionsMap;
        }

        public static Entity GenerateEmptyEntity()
        {
            return new Entity("foo", Rest: null, GraphQL: null, Array.Empty<PermissionSetting>(), Relationships: new(), Mappings: new());
        }

        /// <summary>
        /// Creates an entity with the defined singular and plural entity names.
        /// </summary>
        /// <param name="singularNameForEntity"> Singular name defined by user in the config.</param>
        /// <param name="pluralNameForEntity"> Plural name defined by user in the config.</param>
        public static Entity GenerateEntityWithSingularPlural(string singularNameForEntity, string pluralNameForEntity)
        {
            return new Entity(Source: "foo",
                              Rest: null,
                              GraphQL: new SingularPlural(singularNameForEntity, pluralNameForEntity),
                              Permissions: Array.Empty<PermissionSetting>(),
                              Relationships: new(),
                              Mappings: new());
        }

        /// <summary>
        /// Ensures that for each fieldDefinition present:
        /// - One @authorize directive found
        /// - 1 "roles" argument found on authorize directive
        /// - roles defined on directive are the expected roles defined in runtime configuration
        /// </summary>
        /// <param name="ObjectType">Query or Mutation</param>
        /// <param name="fieldDefinition">Query or Mutation Definition</param>
        public static void ValidateAuthorizeDirectivePresence(string ObjectType, IEnumerable<string> rolesDefinedInPermissions, FieldDefinitionNode fieldDefinition)
        {
            IEnumerable<DirectiveNode> authorizeDirectiveNodesFound = fieldDefinition.Directives.Where(f => f.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE);

            // Currently, only 1 authorize directive node is supported on field definition.
            //
            Assert.AreEqual(expected: 1, actual: authorizeDirectiveNodesFound.Count());

            DirectiveNode authorizationDirectiveNode = authorizeDirectiveNodesFound.First();

            // Possible Arguments: "roles" and "policy" per:
            // https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/AuthorizeDirective.cs
            //
            IEnumerable<ArgumentNode> authorizeArguments = authorizationDirectiveNode.Arguments.Where(f => f.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE_ARGUMENT_ROLES);
            Assert.AreEqual(expected: 1, actual: authorizeArguments.Count());

            ArgumentNode roleArgumentNode = authorizeArguments.First();

            // roleArgumentNode.Value of type IValueNode implemented as a ListValueNode (of role names) for this DirectiveType.
            // ListValueNode collection elements are in the Items property.
            // Items is a collection of IValueNodes which represent role names.
            // Each Item has a Value property of type object, which get casted to a string.
            //
            IEnumerable<string> rolesInRoleArgumentNode = ((ListValueNode)roleArgumentNode.Value).Items.Select(f => (string)f.Value);

            // Ensure expected roles are present in the authorize directive.
            Assert.IsTrue(Enumerable.SequenceEqual(first: rolesDefinedInPermissions, second: rolesInRoleArgumentNode));
        }
    }
}
