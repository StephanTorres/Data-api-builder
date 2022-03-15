using System.Collections.Generic;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataGateway.Service.GraphQLBuilder.Utils;

namespace Azure.DataGateway.Service.GraphQLBuilder.Mutations
{
    internal static class DeleteMutationBuilder
    {
        public static FieldDefinitionNode Build(NameNode name, ObjectTypeDefinitionNode objectTypeDefinitionNode)
        {
            FieldDefinitionNode idField = FindIdField(objectTypeDefinitionNode);
            return new(
                null,
                new NameNode($"delete{name}"),
                new StringValueNode($"Delete a {name}"),
                new List<InputValueDefinitionNode> {
                new InputValueDefinitionNode(
                    null,
                    idField.Name,
                    new StringValueNode($"Id of the item to delete"),
                    new NonNullTypeNode(idField.Type.NamedType()),
                    null,
                    new List<DirectiveNode>())
                },
                new NamedTypeNode(name),
                new List<DirectiveNode>()
            );
        }
    }
}
