using System;
using System.Linq;
using System.Text;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosQueryBuilder : BaseSqlQueryBuilder
    {
        private readonly string _containerAlias = "c";

        /// <summary>
        /// Builds a cosmos sql query string
        /// </summary>
        /// <param name="structure"></param>
        /// <returns></returns>
        public string Build(CosmosQueryStructure structure)
        {
            StringBuilder queryStringBuilder = new();
            queryStringBuilder.Append($"SELECT {WrappedColumns(structure)}"
                + $" FROM {_containerAlias}");
            string predicateString = Build(structure.Predicates);
            if (!string.IsNullOrEmpty(predicateString))
            {
                queryStringBuilder.Append($" WHERE {predicateString}");
            }

            if (structure.OrderByColumns.Count > 0)
            {
                queryStringBuilder.Append($" ORDER BY {Build(structure.OrderByColumns)}");
            }

            return queryStringBuilder.ToString();
        }

        protected override string Build(Column column)
        {
            return _containerAlias + "." + column.ColumnName;
        }

        protected override string Build(KeysetPaginationPredicate? predicate)
        {
            // Cosmos doesnt do keyset pagination
            return string.Empty;
        }

        protected override string QuoteIdentifier(string ident)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// Build columns and wrap columns
        /// </summary>
        private string WrappedColumns(CosmosQueryStructure structure)
        {
            return string.Join(", ",
                structure.Columns.Select(
                    c => _containerAlias + "." + c.Label
            ));
        }

        /// <summary>
        /// Resolves a predicate operation enum to string
        /// </summary>
        protected override string Build(PredicateOperation op)
        {
            switch (op)
            {
                case PredicateOperation.Equal:
                    return "=";
                case PredicateOperation.GreaterThan:
                    return ">";
                case PredicateOperation.LessThan:
                    return "<";
                case PredicateOperation.GreaterThanOrEqual:
                    return ">=";
                case PredicateOperation.LessThanOrEqual:
                    return "<=";
                case PredicateOperation.NotEqual:
                    return "!=";
                case PredicateOperation.AND:
                    return "AND";
                case PredicateOperation.OR:
                    return "OR";
                case PredicateOperation.LIKE:
                    return "LIKE";
                case PredicateOperation.NOT_LIKE:
                    return "NOT LIKE";
                case PredicateOperation.IS:
                    return "";
                case PredicateOperation.IS_NOT:
                    return "NOT";
                default:
                    throw new ArgumentException($"Cannot build unknown predicate operation {op}.");
            }
        }

        /// <summary>
        /// Build left and right predicate operand and resolve the predicate operator into
        /// {OperandLeft} {Operator} {OperandRight}
        /// </summary>
        protected override string Build(Predicate? predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            string predicateString;
            if (Build(predicate.Right).Equals(GQLFilterParser.NullStringValue))
            {
                predicateString = $" {Build(predicate.Op)} IS_NULL({Build(predicate.Left)})";
            }
            else
            {
                predicateString = $"{Build(predicate.Left)} {Build(predicate.Op)} {Build(predicate.Right)}";
            }

            if (predicate.AddParenthesis)
            {
                return "(" + predicateString + ")";
            }
            else
            {
                return predicateString;
            }
        }

    }
}
