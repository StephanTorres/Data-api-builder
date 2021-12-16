using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Implements the mutation engine interface for mutations against Sql like databases.
    /// </summary>
    public class SqlMutationEngine : IMutationEngine
    {
        private readonly IQueryEngine _queryEngine;
        private readonly IMetadataStoreProvider _metadataStoreProvider;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IQueryBuilder _queryBuilder;

        /// <summary>
        /// Constructor
        /// </summary>
        public SqlMutationEngine(IQueryEngine queryEngine, IMetadataStoreProvider metadataStoreProvider, IQueryExecutor queryExecutor, IQueryBuilder queryBuilder)
        {
            _queryEngine = queryEngine;
            _metadataStoreProvider = metadataStoreProvider;
            _queryExecutor = queryExecutor;
            _queryBuilder = queryBuilder;
        }

        /// <summary>
        /// Executes the mutation query and returns result as JSON object asynchronously.
        /// </summary>
        /// <param name="context">context of graphql mutation</param>
        /// <param name="parameters">parameters in the mutation query.</param>
        /// <returns>JSON object result</returns>
        public async Task<JsonDocument> ExecuteAsync(IMiddlewareContext context, IDictionary<string, object> parameters)
        {
            if (context.Selection.Type.IsListType())
            {
                throw new GraphQLUserLevelException("Returning list types from mutations not supported");
            }

            string graphqlMutationName = context.Selection.Field.Name.Value;
            MutationResolver mutationResolver = _metadataStoreProvider.GetMutationResolver(graphqlMutationName);

            ValidateMutationResolver(mutationResolver);

            string queryString;
            Dictionary<string, object> queryParameters;

            switch (mutationResolver.OperationType)
            {
                case "INSERT":
                    SqlInsertStructure insertQueryStruct = new(mutationResolver.Table, parameters, _queryBuilder, _metadataStoreProvider);
                    queryString = insertQueryStruct.ToString();
                    queryParameters = insertQueryStruct.Parameters;
                    break;
                case "UPDATE":
                    SqlUpdateStructure updateQueryStruct = new(mutationResolver.Table, parameters, mutationResolver.UpdateFieldToColumnMappings, _queryBuilder, _metadataStoreProvider);
                    queryString = updateQueryStruct.ToString();
                    queryParameters = updateQueryStruct.Parameters;
                    break;
                default:
                    // this should never trigger, but it is required to make the compiler stop complaining about queryString and queryParameters not being assigned
                    throw new Exception("MutationResolver validator did not catch mutation with invalid operation type");
            }

            Console.WriteLine(queryString);

            using DbDataReader dbDataReader = await _queryExecutor.ExecuteQueryAsync(queryString, queryParameters);

            // scalar type return for mutation not supported / not useful
            if (context.Selection.Type.IsScalarType())
            {
                return null;
            }

            Dictionary<string, object> searchParams = await ExtractRowFromDbDataReader(dbDataReader);

            return await _queryEngine.ExecuteAsync(context, searchParams, false);
        }

        ///<summary>
        /// Extracts a single row from DbDataReader
        ///</summary>
        ///<returns>A dictionary representating the row in <c>ColumnName: Value</c> format</returns>
        private static async Task<Dictionary<string, object>> ExtractRowFromDbDataReader(DbDataReader dbDataReader)
        {
            Dictionary<string, object> row = new();

            if (await dbDataReader.ReadAsync())
            {
                if (dbDataReader.HasRows)
                {
                    DataTable schemaTable = dbDataReader.GetSchemaTable();

                    foreach (DataRow schemaRow in schemaTable.Rows)
                    {
                        string columnName = (string)schemaRow["ColumnName"];
                        row.Add(columnName, dbDataReader[columnName]);
                    }
                }
            }

            return row;
        }

        ///<summary>
        /// Make sure that the MutationResolver has everything needed to resolve the mutation
        ///<summary>
        private static void ValidateMutationResolver(MutationResolver resolver)
        {
            if (resolver.OperationType == null)
            {
                throw new Exception("Mutation resolver must have an OperationType");
            }

            if (resolver.Table == null)
            {
                throw new Exception("Mutation resolver must have a Table");
            }

            switch (resolver.OperationType)
            {
                case "INSERT":
                    break;
                case "UPDATE":
                    if (resolver.UpdateFieldToColumnMappings == null)
                    {
                        throw new Exception("Update mutation must have UpdateFieldToColumnMapptings");
                    }

                    break;
                default:
                    throw new Exception("Invalid OperationType for mutation");

            }
        }
    }
}
