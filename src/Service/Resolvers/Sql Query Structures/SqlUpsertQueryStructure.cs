// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL query resembling an UPSERT operation.
    ///</summary>
    public class SqlUpsertQueryStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Names of columns that will be populated with values during the insert operation.
        /// </summary>
        public List<string> InsertColumns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        /// <summary>
        /// Updates to be applied to selected row
        /// </summary>
        public List<Predicate> UpdateOperations { get; }

        /// <summary>
        /// The columns used for OUTPUT
        /// </summary>
        public List<LabelledColumn> OutputColumns { get; }

        /// <summary>
        /// Indicates whether the upsert should fallback to an update
        /// </summary>
        public bool IsFallbackToUpdate { get; private set; }

        /// <summary>
        /// Maps a column name to the created parameter name to avoid creating
        /// duplicate parameters. Useful in Upsert where an Insert and Update
        /// structure are both created.
        /// </summary>
        private Dictionary<string, string> ColumnToParam { get; }

        /// <summary>
        /// An upsert query must be prepared to be utilized for either an UPDATE or INSERT.
        ///
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="sqlMetadataProvider"></param>
        /// <param name="mutationParams"></param>
        /// <exception cref="DataApiBuilderException"></exception>
        public SqlUpsertQueryStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IDictionary<string, object?> mutationParams,
            bool incrementalUpdate,
            HttpContext httpContext)
        : base(
              metadataProvider: sqlMetadataProvider,
              authorizationResolver: authorizationResolver,
              gQLFilterParser: gQLFilterParser,
              entityName: entityName,
              operationType: Config.Operation.Upsert,
              httpContext: httpContext)
        {
            UpdateOperations = new();
            InsertColumns = new();
            Values = new();
            ColumnToParam = new();
            // All columns will be returned whether upsert results in UPDATE or INSERT
            OutputColumns = GenerateOutputColumns();

            SourceDefinition sourceDefinition = GetUnderlyingSourceDefinition();
            SetFallbackToUpdateOnAutogeneratedPk(sourceDefinition);

            // Populates the UpsertQueryStructure with UPDATE and INSERT column:value metadata
            PopulateColumns(mutationParams, sourceDefinition, isIncrementalUpdate: incrementalUpdate);

            if (FieldsReferencedInDbPolicyForCreateAction.Count > 0)
            {
                // If the size of this set FieldsReferencedInDbPolicyForCreateAction is 0,
                // it implies that all the fields referenced in the database policy for create action are being included in the insert statement, and we are good.
                // However, if the size is non-zero, we throw a bad request exception.
                throw new DataApiBuilderException(
                    message: "One or more fields referenced by the database policy are not present in the request.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }

            if (UpdateOperations.Count == 0)
            {
                throw new DataApiBuilderException(
                    message: "Update mutation does not update any values",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Get the definition of a column by name
        /// </summary>
        public ColumnDefinition GetColumnDefinition(string columnName)
        {
            return GetUnderlyingSourceDefinition().Columns[columnName];
        }

        private void PopulateColumns(
            IDictionary<string, object?> mutationParams,
            SourceDefinition sourceDefinition,
            bool isIncrementalUpdate)
        {
            List<string> primaryKeys = sourceDefinition.PrimaryKey;
            List<string> schemaColumns = sourceDefinition.Columns.Keys.ToList();

            try
            {
                foreach (KeyValuePair<string, object?> param in mutationParams)
                {
                    // since we have already validated mutationParams we know backing column exists
                    MetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
                    // Create Parameter and map it to column for downstream logic to utilize.
                    string paramIdentifier;
                    if (param.Value is not null)
                    {
                        paramIdentifier = MakeParamWithValue(GetParamAsSystemType(param.Value.ToString()!, backingColumn!, GetColumnSystemType(backingColumn!)));
                    }
                    else
                    {
                        paramIdentifier = MakeParamWithValue(null);
                    }

                    ColumnToParam.Add(backingColumn!, paramIdentifier);

                    // Create a predicate for UPDATE Operation.
                    Predicate predicate = new(
                        new PredicateOperand(new Column(tableSchema: DatabaseObject.SchemaName, tableName: DatabaseObject.Name, columnName: backingColumn!)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"{paramIdentifier}")
                    );

                    // We are guaranteed by the RequestValidator, that a primary key column is in the URL, not body.
                    // That means we must add the PK as predicate for the update request,
                    // as Update request uses Where clause to target item by PK.
                    if (primaryKeys.Contains(backingColumn!))
                    {
                        PopulateColumnsAndParams(backingColumn!);

                        // PK added as predicate for Update Operation
                        Predicates.Add(predicate);

                        // Track which columns we've acted upon,
                        // so we can add nullified remainder columns later.
                        schemaColumns.Remove(backingColumn!);
                    }
                    // No need to check param.key exists in schema as invalid columns are caught in RequestValidation.
                    else
                    {
                        // Update Operation. Add since mutation param is not a PK.
                        UpdateOperations.Add(predicate);
                        schemaColumns.Remove(backingColumn!);

                        // Insert Operation, create record with request specified value.
                        PopulateColumnsAndParams(backingColumn!);
                    }
                }

                // Process remaining columns in schemaColumns.
                if (isIncrementalUpdate)
                {
                    SetFallbackToUpdateOnMissingColumInPatch(schemaColumns, sourceDefinition);
                }
                else
                {
                    // UpdateOperations will be modified and have nullable values added for update when appropriate
                    AddNullifiedUnspecifiedFields(schemaColumns, UpdateOperations, sourceDefinition);
                }
            }
            catch (ArgumentException ex)
            {
                // ArgumentException thrown from GetParamAsColumnSystemType()
                throw new DataApiBuilderException(
                    message: ex.Message,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: ex);
            }
        }

        /// <summary>
        /// Populates the column name in Columns, gets created parameter
        /// and adds its value to Values.
        /// </summary>
        /// <param name="columnName">The name of the column.</param>
        private void PopulateColumnsAndParams(string columnName)
        {
            InsertColumns.Add(columnName);

            // As we add columns to the InsertColumns list for SqlUpsertQueryStructure one by one,
            // we remove the columns (if present) from the FieldsReferencedInDbPolicyForCreateAction.
            // This is necessary because any field referenced in database policy but not present in insert statement would result in an exception.
            FieldsReferencedInDbPolicyForCreateAction.Remove(columnName);
            string paramName;
            paramName = ColumnToParam[columnName];
            Values.Add($"{paramName}");
        }

        /// <summary>
        /// Sets the value of fallback to update by checking if the pk of the table is autogenerated
        /// </summary>
        /// <param name="tableDef"></param>
        private void SetFallbackToUpdateOnAutogeneratedPk(SourceDefinition tableDef)
        {
            bool pkIsAutogenerated = false;
            foreach (string primaryKey in tableDef.PrimaryKey)
            {
                if (tableDef.Columns[primaryKey].IsAutoGenerated)
                {
                    pkIsAutogenerated = true;
                    break;
                }
            }

            IsFallbackToUpdate = pkIsAutogenerated;
        }

        /// <summary>
        /// Sets the value of fallback to update by checking if any required column (non autogenerated, non default, non nullable)
        /// is missing during PATCH
        /// </summary>
        /// <param name="tableDef"></param>
        private void SetFallbackToUpdateOnMissingColumInPatch(List<string> leftoverSchemaColumns, SourceDefinition tableDef)
        {
            foreach (string leftOverColumn in leftoverSchemaColumns)
            {
                if (!tableDef.Columns[leftOverColumn].IsAutoGenerated
                    && !tableDef.Columns[leftOverColumn].HasDefault
                    && !tableDef.Columns[leftOverColumn].IsNullable)
                {
                    IsFallbackToUpdate = true;
                    break;
                }
            }
        }
    }
}
