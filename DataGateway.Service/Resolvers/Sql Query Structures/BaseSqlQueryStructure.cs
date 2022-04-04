using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{

    /// <summary>
    /// Holds shared properties and methods among
    /// Sql*QueryStructure classes
    /// </summary>
    public abstract class BaseSqlQueryStructure : BaseQueryStructure
    {
        protected SqlGraphQLFileMetadataProvider MetadataStoreProvider { get; }

        /// <summary>
        /// The name of the main table to be queried.
        /// </summary>
        public string TableName { get; protected set; }
        /// <summary>
        /// The alias of the main table to be queried.
        /// </summary>
        public string TableAlias { get; protected set; }

        /// <summary>
        /// FilterPredicates is a string that represents the filter portion of our query
        /// in the WHERE Clause. This is generated specifically from the $filter portion
        /// of the query string.
        /// </summary>
        public string? FilterPredicates { get; set; }

        public BaseSqlQueryStructure(
            SqlGraphQLFileMetadataProvider metadataStoreProvider,
            IncrementingInteger? counter = null,
            string tableName = "")
            : base(counter)
        {
            MetadataStoreProvider = metadataStoreProvider;

            TableName = tableName;
            // Default the alias to the table name
            TableAlias = tableName;
        }

        /// <summary>
        /// For UPDATE (OVERWRITE) operation
        /// Adds result of (TableDefinition.Columns minus MutationFields) to UpdateOperations with null values
        /// There will not be any columns leftover that are PK, since they are handled in request validation.
        /// </summary>
        /// <param name="leftoverSchemaColumns"></param>
        /// <param name="updateOperations">List of Predicates representing UpdateOperations.</param>
        /// <param name="tableDefinition">The definition for the table.</param>
        public void AddNullifiedUnspecifiedFields(List<string> leftoverSchemaColumns, List<Predicate> updateOperations, TableDefinition tableDefinition)
        {
            //result of adding (TableDefinition.Columns - MutationFields) to UpdateOperations
            foreach (string leftoverColumn in leftoverSchemaColumns)
            {
                // If the left over column is autogenerated or has a default,
                // then no need to add it with a null value.
                if (tableDefinition.Columns[leftoverColumn].IsAutoGenerated
                    || tableDefinition.Columns[leftoverColumn].HasDefault)
                {
                    continue;
                }

                if (tableDefinition.Columns[leftoverColumn].IsNullable)
                {
                    Predicate predicate = new(
                        new PredicateOperand(new Column(null, leftoverColumn)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(null)}")
                    );

                    updateOperations.Add(predicate);
                }
                else
                {
                    // Non Nullable columns must have a value defined in request unless
                    // the table schema configures the column with a default value.
                    throw new DataGatewayException(
                        message: "Request must define values for all nullable and non-default valued columns.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
                }

            }
        }

        /// <summary>
        /// Get column type from table underlying the query strucutre
        /// </summary>
        public Type GetColumnSystemType(string columnName)
        {
            if (GetTableDefinition().Columns.TryGetValue(columnName, out ColumnDefinition? column))
            {
                return column.SystemType;
            }
            else
            {
                throw new ArgumentException($"{columnName} is not a valid column of {TableName}");
            }
        }

        /// <summary>
        /// Returns the TableDefinition for the the table of this query.
        /// </summary>
        protected TableDefinition GetTableDefinition()
        {
            return MetadataStoreProvider.GetTableDefinition(TableName);
        }

        /// <summary>
        /// Get primary key as list of string
        /// </summary>
        public List<string> PrimaryKey()
        {
            return GetTableDefinition().PrimaryKey;
        }

        /// <summary>
        /// get all columns of the table
        /// </summary>
        public List<string> AllColumns()
        {
            return GetTableDefinition().Columns.Select(col => col.Key).ToList();
        }

        ///<summary>
        /// Gets the value of the parameter cast as the system type
        /// of the column this parameter is associated with
        ///</summary>
        /// <exception cref="ArgumentException">columnName is not a valid column of table or param
        /// does not have a valid value type</exception>
        protected object GetParamAsColumnSystemType(string param, string columnName)
        {
            Type systemType = GetColumnSystemType(columnName);
            try
            {
                switch (systemType.Name)
                {
                    case "String":
                        return param;
                    case "Int64":
                        return long.Parse(param);
                    default:
                        // should never happen due to the config being validated for correct types
                        throw new NotSupportedException($"{systemType.Name} is not supported");
                }
            }
            catch (Exception e)
            {
                if (e is FormatException ||
                    e is ArgumentNullException ||
                    e is OverflowException)
                {
                    throw new ArgumentException($"Parameter \"{param}\" cannot be resolved as column \"{columnName}\" " +
                        $"with type \"{systemType.Name}\".");
                }

                throw;
            }
        }
    }
}
