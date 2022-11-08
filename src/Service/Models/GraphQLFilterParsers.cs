using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.Models
{
    /// <summary>
    /// Contains methods to parse a GQL filter parameter
    /// </summary>
    public static class GQLFilterParser
    {
        public static readonly string NullStringValue = "NULL";

        /// <summary>
        /// Parse a predicate for a *FilterInput input type
        /// </summary>
        /// <param name="ctx">The GraphQL context, used to get the query variables</param>
        /// <param name="filterArgumentSchema">An IInputField object which describes the schema of the filter argument</param>
        /// <param name="fields">The fields in the *FilterInput being processed</param>
        /// <param name="sourceAlias">The source alias underlyin the *FilterInput being processed</param>
        /// <param name="sourceDefinition">Definition of the table/view underlying the *FilterInput being processed</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        public static Predicate Parse(
            IMiddlewareContext ctx,
            IInputField filterArgumentSchema,
            List<ObjectFieldNode> fields,
            string schemaName,
            string sourceName,
            string sourceAlias,
            SourceDefinition sourceDefinition,
            Func<object, string> processLiterals)
        {
            InputObjectType filterArgumentObject = ResolverMiddleware.InputObjectTypeFromIInputField(filterArgumentSchema);

            List<PredicateOperand> predicates = new();
            foreach (ObjectFieldNode field in fields)
            {
                object? fieldValue = ResolverMiddleware.ExtractValueFromIValueNode(
                    value: field.Value,
                    argumentSchema: filterArgumentObject.Fields[field.Name.Value],
                    variables: ctx.Variables);

                if (fieldValue is null)
                {
                    continue;
                }

                string name = field.Name.ToString();

                bool fieldIsAnd = string.Equals(name, $"{PredicateOperation.AND}", StringComparison.OrdinalIgnoreCase);
                bool fieldIsOr = string.Equals(name, $"{PredicateOperation.OR}", StringComparison.OrdinalIgnoreCase);

                InputObjectType filterInputObjectType = ResolverMiddleware.InputObjectTypeFromIInputField(filterArgumentObject.Fields[name]);
                if (fieldIsAnd || fieldIsOr)
                {
                    PredicateOperation op = fieldIsAnd ? PredicateOperation.AND : PredicateOperation.OR;

                    List<IValueNode> otherPredicates = (List<IValueNode>)fieldValue;
                    predicates.Push(new PredicateOperand(ParseAndOr(
                        ctx,
                        argumentSchema: filterArgumentObject.Fields[name],
                        filterArgumentSchema: filterArgumentSchema,
                        otherPredicates,
                        schemaName,
                        sourceName,
                        sourceAlias,
                        sourceDefinition,
                        op,
                        processLiterals)));
                }
                else
                {
                    List<ObjectFieldNode> subfields = (List<ObjectFieldNode>)fieldValue;

                    if (!IsSingularType(filterInputObjectType.Name))
                    {
                        // For SQL,
                        if (sourceDefinition.PrimaryKey.Count != 0)
                        {
                            // if there are primary keys on the source, we need to perform a join
                            // between the source and the non-scalar filter entity.
                            InputField filterField = filterArgumentObject.Fields[name];

                            string? targetEntityForFilter;
                            _ = RelationshipDirectiveType.Target(filterField);

                            /* if (GraphQLUtils.TryExtractGraphQLFieldModelName(_underlyingFieldType.Directives, out string? modelName))
                             {
                                 EntityName = modelName;
                             }

                             DatabaseObject.SchemaName = sqlMetadataProvider.GetSchemaName(EntityName);
                             DatabaseObject.Name = sqlMetadataProvider.GetDatabaseObjectName(EntityName);
                             Predicate predicateOnExists = Parse(
                                 ctx,
                                 argumentSchema: filterArgumentObject.Fields[name],
                                 fields: subfields,
                                 );*/
                            // Recursively parse and obtain the predicates for the Exists clause subquery
                            // Create a SqlQueryStructure as the predicate operand of Exists predicate with no order by, no limit, select 1
                            // - with predicates = the predicate obtained from recursively parsing
                            // Add JoinPredicates to the subquery query structure so a predicate connecting
                            // the outer table is added to the where clause of subquery
                            // make chained predicate using this exists predicate and return.
                            // Handle Exist clause while Building each of the Predicates.
                            // Build the Exists predicate subquery using special Build of SqlQueryStructure

                            // Recursively parse and obtain the predicates for the join subquery to add to the Joins property of type List<SqlJoinStructure>
                            // - with predicates = the predicate obtained from recursively parsing and a predicate relating the two entities
                            // continue with rest of the filters
                            // Handle join clause while Building the original query structure
                        }
                        else
                        {
                            return Parse(ctx,
                                filterArgumentObject.Fields[name],
                                subfields,
                                schemaName, sourceName + "." + name, sourceAlias + "." + name, sourceDefinition, processLiterals);
                        }
                    }
                    else
                    {
                        predicates.Push(new PredicateOperand(ParseScalarType(
                            ctx,
                            argumentSchema: filterArgumentObject.Fields[name],
                            name,
                            subfields,
                            schemaName,
                            sourceName,
                            sourceAlias,
                            processLiterals)));
                    }
                }
            }

            return MakeChainPredicate(predicates, PredicateOperation.AND);
        }

        static bool IsSingularType(string name)
        {
            return new string[] { "StringFilterInput", "IntFilterInput", "BoolFilterInput", "IdFilterInput" }.Contains(name);
        }

        /// <summary>
        /// Calls the appropriate scalar type filter parser based on the type of
        /// the fields
        /// </summary>
        /// <param name="ctx">The GraphQL context, used to get the query variables</param>
        /// <param name="argumentSchema">An IInputField object which describes the schema of the scalar input argument (e.g. IntFilterInput)</param>
        /// <param name="name">The name of the field</param>
        /// <param name="fields">The subfields of the scalar field</param>
        /// <param name="schemaName">The db schema name to which the table belongs</param>
        /// <param name="tableName">The name of the table underlying the *FilterInput being processed</param>
        /// <param name="tableAlias">The alias of the table underlying the *FilterInput being processed</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        private static Predicate ParseScalarType(
            IMiddlewareContext ctx,
            IInputField argumentSchema,
            string name,
            List<ObjectFieldNode> fields,
            string schemaName,
            string tableName,
            string tableAlias,
            Func<object, string> processLiterals)
        {
            Column column = new(schemaName, tableName, columnName: name, tableAlias);

            return FieldFilterParser.Parse(ctx, argumentSchema, column, fields, processLiterals);
        }

        /// <summary>
        /// Parse the list of *FilterInput objects passed in and/or fields into a single predicate
        /// </summary>
        /// <returns>
        /// The predicate representation of the and/or.
        /// If and/or is passed as empty, a predicate representing 1 != 1 is returned
        /// </returns>
        /// <param name="ctx">The GraphQL context, used to get the query variables</param>
        /// <param name="argumentSchema">An IInputField object which describes the and/or filter input argument</param>
        /// <param name="filterArgumentSchema">An IInputField object which describes the base filter input argument (e.g. BookFilterInput)
        /// to which the and/or belongs </param>
        /// <param name="fields">The subfields of the and/or field</param>
        /// <param name="schemaName">The db schema name to which the table belongs</param>
        /// <param name="tableName">The name of the table underlying the *FilterInput being processed</param>
        /// <param name="tableAlias">The alias of the table underlying the *FilterInput being processed</param>
        /// <param name="sourceDefinition">Definition of the table/view underlying the *FilterInput being processed</param>
        /// <param name="op">The operation (and or or)</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        private static Predicate ParseAndOr(
            IMiddlewareContext ctx,
            IInputField argumentSchema,
            IInputField filterArgumentSchema,
            List<IValueNode> fields,
            string schemaName,
            string tableName,
            string tableAlias,
            SourceDefinition sourceDefinition,
            PredicateOperation op,
            Func<object, string> processLiterals)
        {
            if (fields.Count == 0)
            {
                return Predicate.MakeFalsePredicate();
            }

            List<PredicateOperand> operands = new();
            foreach (IValueNode field in fields)
            {
                object? fieldValue = ResolverMiddleware.ExtractValueFromIValueNode(
                    value: field,
                    argumentSchema: argumentSchema,
                    ctx.Variables);

                if (fieldValue is null)
                {
                    continue;
                }

                List<ObjectFieldNode> subfields = (List<ObjectFieldNode>)fieldValue;
                operands.Add(new PredicateOperand(Parse(ctx, filterArgumentSchema, subfields, schemaName, tableName, tableAlias, sourceDefinition, processLiterals)));
            }

            return MakeChainPredicate(operands, op);
        }

        /// <summary>
        /// Merge a list of predicate operands into a single predicate
        /// </summary>
        /// <param name="operands">A list of PredicateOperands to be connected with a PredicateOperation</param>
        /// <param name="op">An operation used to connect the predicate operands</param>
        /// <param name="pos">No need to specify this parameter, it is used to make the method recursive</param>
        /// <param name="addParenthesis">Specify whether the final predicate should be put in parenthesis or not</param>
        public static Predicate MakeChainPredicate(
            List<PredicateOperand> operands,
            PredicateOperation op,
            int pos = 0,
            bool addParenthesis = true)
        {
            if (operands.Count == 0)
            {
                return Predicate.MakeFalsePredicate();
            }

            if (pos == operands.Count - 1)
            {
                return operands[pos].AsPredicate()!;
            }

            return new Predicate(
                operands[pos],
                op,
                new PredicateOperand(MakeChainPredicate(operands, op, pos + 1, false)),
                addParenthesis: addParenthesis && operands.Count > 1
            );
        }
    }

    public static class FieldFilterParser
    {
        /// <summary>
        /// Parse a scalar field into a predicate
        /// </summary>
        /// <param name="ctx">The GraphQL context, used to get the query variables</param>
        /// <param name="argumentSchema">An IInputField object which describes the schema of the scalar input argument (e.g. IntFilterInput)</param>
        /// <param name="column">The table column targeted by the field</param>
        /// <param name="fields">The subfields of the scalar field</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        public static Predicate Parse(
            IMiddlewareContext ctx,
            IInputField argumentSchema,
            Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();

            InputObjectType argumentObject = ResolverMiddleware.InputObjectTypeFromIInputField(argumentSchema);
            foreach (ObjectFieldNode field in fields)
            {
                string name = field.Name.ToString();
                object? value = ResolverMiddleware.ExtractValueFromIValueNode(
                    value: field.Value,
                    argumentSchema: argumentObject.Fields[field.Name.Value],
                    variables: ctx.Variables);

                bool processLiteral = true;

                if (value is null)
                {
                    continue;
                }

                PredicateOperation op;
                switch (name)
                {
                    case "eq":
                        op = PredicateOperation.Equal;
                        break;
                    case "neq":
                        op = PredicateOperation.NotEqual;
                        break;
                    case "lt":
                        op = PredicateOperation.LessThan;
                        break;
                    case "gt":
                        op = PredicateOperation.GreaterThan;
                        break;
                    case "lte":
                        op = PredicateOperation.LessThanOrEqual;
                        break;
                    case "gte":
                        op = PredicateOperation.GreaterThanOrEqual;
                        break;
                    case "contains":
                        op = PredicateOperation.LIKE;
                        value = $"%{EscapeLikeString((string)value)}%";
                        break;
                    case "notContains":
                        op = PredicateOperation.NOT_LIKE;
                        value = $"%{EscapeLikeString((string)value)}%";
                        break;
                    case "startsWith":
                        op = PredicateOperation.LIKE;
                        value = $"{EscapeLikeString((string)value)}%";
                        break;
                    case "endsWith":
                        op = PredicateOperation.LIKE;
                        value = $"%{EscapeLikeString((string)value)}";
                        break;
                    case "isNull":
                        processLiteral = false;
                        bool isNull = (bool)value;
                        op = isNull ? PredicateOperation.IS : PredicateOperation.IS_NOT;
                        value = GQLFilterParser.NullStringValue;
                        break;
                    default:
                        throw new NotSupportedException($"Operation {name} on int type not supported.");
                }

                predicates.Push(new PredicateOperand(new Predicate(
                    new PredicateOperand(column),
                    op,
                    new PredicateOperand(processLiteral ? $"@{processLiterals(value)}" : value.ToString()))
                ));
            }

            return GQLFilterParser.MakeChainPredicate(predicates, PredicateOperation.AND);
        }

        private static string EscapeLikeString(string input)
        {
            input = input.Replace(@"\", @"\\");
            input = input.Replace(@"%", @"\%");
            input = input.Replace(@"[", @"\[");
            input = input.Replace(@"]", @"\]");
            input = input.Replace(@"_", @"\_");
            return input;
        }
    }
}
