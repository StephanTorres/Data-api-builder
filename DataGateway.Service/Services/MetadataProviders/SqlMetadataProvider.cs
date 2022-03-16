using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Reads schema information from the database to make it
    /// available for the GraphQL/REST services.
    /// </summary>
    public class SqlMetadataProvider<ConnectionT, DataAdapterT, CommandT>
        where ConnectionT : DbConnection, new()
        where DataAdapterT : DbDataAdapter, new()
        where CommandT : DbCommand, new()
    {
        private const int NUMBER_OF_RESTRICTIONS = 4;
        protected const string TABLE_TYPE = "BASE TABLE";

        protected string ConnectionString { get; init; }

        protected DataSet EntitiesDataSet { get; init; }

        public SqlMetadataProvider(string connectionString)
        {
            ConnectionString = connectionString;
            EntitiesDataSet = new();
        }

        /// <summary>
        /// Gets the DataTable from the EntitiesDataSet if already present.
        /// If not present, fills it first and returns the same.
        /// </summary>
        public virtual async Task<DataTable> GetTableWithSchemaFromDataSet(
            string schemaName,
            string tableName)
        {
            DataTable? dataTable = EntitiesDataSet.Tables[tableName];
            if (dataTable == null)
            {
                dataTable = await FillSchemaForTable(schemaName, tableName);
            }

            return dataTable;
        }

        /// <summary>
        /// Fills the table definition with information of all columns and
        /// primary keys.
        /// </summary>
        /// <param name="schemaName">Name of the schema.</param>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="tableDefinition">Table definition to fill.</param>
        public virtual async Task PopulateTableDefinition(
            string schemaName,
            string tableName,
            TableDefinition tableDefinition)
        {
            DataTable dataTable = await GetTableWithSchemaFromDataSet(schemaName, tableName);

            List<DataColumn> primaryKeys = new(dataTable.PrimaryKey);
            tableDefinition.PrimaryKey = new(primaryKeys.Select(primaryKey => primaryKey.ColumnName));

            using DataTableReader reader = new(dataTable);
            DataTable schemaTable = reader.GetSchemaTable();
            foreach (DataRow columnInfoFromAdapter in schemaTable.Rows)
            {
                string columnName = columnInfoFromAdapter["ColumnName"].ToString()!;
                ColumnDefinition column = new();
                column.IsNullable = (bool)columnInfoFromAdapter["AllowDBNull"];
                column.IsAutoGenerated = (bool)columnInfoFromAdapter["IsAutoIncrement"];
                column.SystemType = (Type)columnInfoFromAdapter["DataType"];
                tableDefinition.Columns.Add(columnName, column);
            }

            await PopulateColumnDefinitionWithHasDefaultAsync(
                schemaName,
                tableName,
                tableDefinition);
        }

        /// <summary>
        /// Populates the column definition with HasDefault property.
        /// </summary>
        protected virtual async Task PopulateColumnDefinitionWithHasDefaultAsync(
            string schemaName,
            string tableName,
            TableDefinition tableDefinition)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            // We can specify the Catalog, Schema, Table Name, Column Name to get
            // the specified column(s).
            // Hence, we should create a 4 members array.
            string[] columnRestrictions = new string[NUMBER_OF_RESTRICTIONS];

            // To restrict the columns for the current table, specify the table's name
            // in column restrictions.
            columnRestrictions[0] = conn.Database;
            columnRestrictions[1] = schemaName;
            columnRestrictions[2] = tableName;

            // Each row in the columnsInTable table corresponds to a single column of the table.
            DataTable columnsInTable = await conn.GetSchemaAsync("Columns", columnRestrictions);

            foreach (DataRow columnInfo in columnsInTable.Rows)
            {
                string columnName = (string)columnInfo["COLUMN_NAME"];
                bool hasDefault = !string.IsNullOrEmpty(columnInfo["COLUMN_DEFAULT"].ToString());
                ColumnDefinition? columnDefinition;
                if (tableDefinition.Columns.TryGetValue(columnName, out columnDefinition))
                {
                    columnDefinition.HasDefault = hasDefault;
                }
            }
        }

        /// <summary>
        /// Using a data adapter, obtains the schema of the given table name
        /// and adds the corresponding entity in the data set.
        /// </summary>
        protected virtual async Task<DataTable> FillSchemaForTable(
            string schemaName,
            string tableName)
        {
            using ConnectionT conn = new();
            conn.ConnectionString = ConnectionString;
            await conn.OpenAsync();

            DataAdapterT adapterForTable = new();
            CommandT selectCommand = new();
            selectCommand.Connection = conn;
            selectCommand.CommandText = ($"SELECT * FROM {schemaName}.{tableName}");
            adapterForTable.SelectCommand = selectCommand;

            DataTable[] dataTable = adapterForTable.FillSchema(EntitiesDataSet, SchemaType.Source, tableName);
            return dataTable[0];
        }
    }
}
