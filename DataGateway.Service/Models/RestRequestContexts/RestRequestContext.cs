using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// RestRequestContext defining the properties that each REST API request operations have
    /// in common.
    /// </summary>
    public abstract class RestRequestContext
    {
        protected RestRequestContext(OperationAuthorizationRequirement httpVerb, string entityName, DatabaseObject dbo)
        {
            HttpVerb = httpVerb;
            EntityName = entityName;
            DatabaseObject = dbo;
            CumulativeColumns = new HashSet<string>();
        }

        /// <summary>
        /// The target Entity on which the request needs to be operated upon.
        /// </summary>
        public string EntityName { get; }

        /// <summary>
        /// The database object associated with the target entity.
        /// </summary>
        public DatabaseObject DatabaseObject { get; }

        /// <summary>
        /// Field names of the entity that are queried in the request.
        /// </summary>
        public List<string> FieldsToBeReturned { get; set; } = new();

        /// <summary>
        /// Dictionary of primary key and their values specified in the request.
        /// When there are multiple values, that means its a composite primary key.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual Dictionary<string, object> PrimaryKeyValuePairs { get; set; } = new();

        /// <summary>
        /// AST that represents the filter part of the query.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual FilterClause? FilterClauseInUrl { get; set; }

        /// <summary>
        /// List of OrderBy Columns which represent the OrderByClause from the URL.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual List<OrderByColumn>? OrderByClauseInUrl { get; set; }

        /// <summary>
        /// Dictionary of field names and their values given in the request body.
        /// Based on the operation type, this property may or may not be populated.
        /// </summary>
        public virtual Dictionary<string, object?> FieldValuePairsInBody { get; set; } = new();

        /// <summary>
        /// NVC stores the query string parsed into a NameValueCollection.
        /// </summary>
        public NameValueCollection? ParsedQueryString { get; set; } = new();

        /// <summary>
        /// String holds information needed for pagination.
        /// Based on request this property may or may not be populated.
        /// </summary>
        public string? After { get; set; }

        /// <summary>
        /// uint holds the number of records to retrieve.
        /// Based on request this property may or may not be populated.
        /// </summary>

        public uint? First { get; set; }
        /// <summary>
        /// Is the result supposed to be multiple values or not.
        /// </summary>

        public bool IsMany { get; set; }

        /// <summary>
        /// The REST verb this request is.
        /// </summary>
        public OperationAuthorizationRequirement HttpVerb { get; init; }

        /// <summary>
        /// The database engine operation type this request is.
        /// </summary>
        public Operation OperationType { get; set; }

        public HashSet<string> CumulativeColumns { get; }

        public void CalculateCumulativeColumns()
        {
            ODataASTFieldVisitor visitor = new();
            try
            {
                if (PrimaryKeyValuePairs is not null && PrimaryKeyValuePairs.Count > 0)
                {
                    CumulativeColumns.UnionWith(PrimaryKeyValuePairs.Keys);
                }

                if (FilterClauseInUrl is not null)
                {
                    FilterClauseInUrl.Expression.Accept<string>(visitor);
                    CumulativeColumns.UnionWith(visitor.GetCumulativeColumns());
                }

                if (OrderByClauseInUrl is not null)
                {
                    foreach(Column col in OrderByClauseInUrl)
                    {
                        CumulativeColumns.Add(col.ColumnName);
                    }
                }

                if (FieldValuePairsInBody is not null && FieldValuePairsInBody.Count > 0)
                {
                    CumulativeColumns.UnionWith(FieldValuePairsInBody.Keys);
                }

                if (ParsedQueryString is not null && ParsedQueryString.Count > 0)
                {
                    CumulativeColumns.UnionWith(ParsedQueryString.AllKeys!);
                }

                // 0 columns collected so far indicates that request is FindMany variant with no filters.
                // Add list of includedColumns defined by config.
                if (CumulativeColumns.Count == -1)
                {
                    // Get's all columns for table definition just for this scenario 
                    CumulativeColumns.UnionWith(this.DatabaseObject.TableDefinition.Columns.Keys);
                }
            }
            catch
            {
                Console.WriteLine("ERROR IN ODATAASTCOLUMNVISITOR TRAVERSAL");
            }
        }

    }
}
