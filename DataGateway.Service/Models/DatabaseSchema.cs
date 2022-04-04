using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Authorization;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// The schema of the database described in a JSON format.
    /// </summary>
    public class DatabaseSchema
    {
        public Dictionary<string, TableDefinition> Tables { get; set; } =
            new(StringComparer.InvariantCultureIgnoreCase);
    }

    public class TableDefinition
    {
        /// <summary>
        /// The list of columns that together form the primary key of the table.
        /// </summary>
        public List<string> PrimaryKey { get; set; } = new();
        public Dictionary<string, ColumnDefinition> Columns { get; set; } =
            new(StringComparer.InvariantCultureIgnoreCase);
        public Dictionary<string, ForeignKeyDefinition> ForeignKeys { get; set; } = new();
        public Dictionary<string, AuthorizationRule> HttpVerbs { get; set; } = new();
    }

    public class ColumnDefinition
    {
        /// <summary>
        /// The database type of this column mapped to the SystemType.
        /// </summary>
        public Type SystemType { get; set; } = typeof(object);
        public bool HasDefault { get; set; }
        public bool IsAutoGenerated { get; set; }
        public bool IsNullable { get; set; }
        public object? DefaultValue { get; set; }
    }

    public class ForeignKeyDefinition
    {
        public string ReferencedTable { get; set; } = string.Empty;

        /// <summary>
        /// The list of columns referenced in the reference table.
        /// If this list is empty, the primary key columns of the referenced
        /// table are implicitly assumed to be the referenced columns.
        /// </summary>
        public List<string> ReferencedColumns { get; set; } = new();

        /// <summary>
        /// The list of columns of the table that make up the foreign key.
        /// If this list is empty, the primary key columns of the
        /// table are implicitly assumed to be the foreign key columns.
        /// </summary>
        public List<string> Columns { get; set; } = new();
    }

    public class AuthorizationRule
    {
        /// <summary>
        /// The various type of AuthZ scenarios supported: Anonymous, Authenticated.
        /// </summary>
        public AuthorizationType AuthorizationType { get; set; }
    }
}
