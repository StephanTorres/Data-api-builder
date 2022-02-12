using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosQueryStructure : BaseSqlQueryStructure
    {
        private IMiddlewareContext _context;
        public bool IsPaginated { get; internal set; }

        private readonly string _containerAlias = "c";
        public string Container { get; internal set; }
        public string Database { get; internal set; }
        public string Continuation { get; internal set; }
        public long MaxItemCount { get; internal set; }

        public CosmosQueryStructure(IMiddlewareContext context,
            IDictionary<string, object> parameters,
            IMetadataStoreProvider metadataStoreProvider) : base(metadataStoreProvider)
        {
            _context = context;
            Init(parameters);
        }

        private void Init(IDictionary<string, object> queryParams)
        {
            IFieldSelection selection = _context.Selection;
            GraphqlType graphqlType = MetadataStoreProvider.GetGraphqlType(UnderlyingType(selection.Field.Type).Name);
            IsPaginated = graphqlType.IsPaginationType;

            if (IsPaginated)
            {
                FieldNode fieldNode = ExtractItemsQueryField(selection.SyntaxNode);
                graphqlType = MetadataStoreProvider.GetGraphqlType(UnderlyingType((ExtractItemsSchemaField(selection.Field)).Type).Name);

                Columns.AddRange(fieldNode.SelectionSet.Selections.Select(x => new LabelledColumn(_containerAlias, "", x.ToString())));
            }
            else
            {
                Columns.AddRange(selection.SyntaxNode.SelectionSet.Selections.Select(x => new LabelledColumn(_containerAlias, "", x.ToString())));
            }

            Container = graphqlType.Container;
            Database = graphqlType.Database;

            foreach (KeyValuePair<string, object> parameter in queryParams)
            {
                // first and after will not be part of query parameters. They will be going into headers instead.
                // TODO: Revisit 'first' while adding support for TOP queries
                if (parameter.Key == "first")
                {
                    MaxItemCount = (long)parameter.Value;
                    continue;
                }

                if (parameter.Key == "after")
                {
                    Continuation = (string)parameter.Value;
                    continue;
                }

                Predicates.Add(new Predicate(
                    new PredicateOperand(new Column(_containerAlias, parameter.Key)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(parameter.Value)}")
                ));
            }
        }
    }
}
