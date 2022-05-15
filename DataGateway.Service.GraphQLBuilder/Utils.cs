using Azure.DataGateway.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.GraphQLBuilder
{
    internal static class Utils
    {
        public static bool IsModelType(ObjectTypeDefinitionNode objectTypeDefinitionNode)
        {
            string modelDirectiveName = ModelDirectiveType.DirectiveName;
            return objectTypeDefinitionNode.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
        }

        public static bool IsModelType(ObjectType objectType)
        {
            string modelDirectiveName = ModelDirectiveType.DirectiveName;
            return objectType.Directives.Any(d => d.Name.ToString() == modelDirectiveName);
        }

        public static bool IsBuiltInType(ITypeNode typeNode)
        {
            string name = typeNode.NamedType().Name.Value;
            if (name == "String" || name == "Int" || name == "Boolean" || name == "Float" || name == "ID")
            {
                return true;
            }

            return false;
        }

        public static List<FieldDefinitionNode> FindPrimaryKeyFields(ObjectTypeDefinitionNode node)
        {
            List<FieldDefinitionNode> fieldDefinitionNodes =
                new(node.Fields.Where(f => f.Directives.Any(d => d.Name.Value == PrimaryKeyDirectiveType.DirectiveName)));

            // By convention we look for a `@primaryKey` directive, if that didn't exist
            // fallback to using an expected field name on the GraphQL object
            if (fieldDefinitionNodes.Count == 0)
            {
                FieldDefinitionNode? fieldDefinitionNode =
                    node.Fields.FirstOrDefault(f => f.Name.Value == "id");
                if (fieldDefinitionNode is not null)
                {
                    fieldDefinitionNodes.Add(fieldDefinitionNode);
                }
            }

            // Nothing explicitly defined nor could we find anything using our conventions, fail out
            if (fieldDefinitionNodes.Count == 0)
            {
                // TODO: Proper exception type
                throw new Exception("No primary key defined and conventions couldn't locate a fallback");
            }

            return fieldDefinitionNodes;
        }

        /// <summary>
        /// Checks if a field is auto generated by the database using the directives of the field definition.
        /// </summary>
        /// <param name="field">Field definition to check.</param>
        /// <returns><c>true</c> if it is auto generated, <c>false</c> if it is not.</returns>
        public static bool IsAutoGeneratedField(FieldDefinitionNode field)
        {
            return field.Directives.Any(d => d.Name.Value == AutoGeneratedDirectiveType.DirectiveName);
        }
    }
}
