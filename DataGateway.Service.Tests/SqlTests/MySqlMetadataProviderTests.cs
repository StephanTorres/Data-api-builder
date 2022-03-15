using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlMetadataProviderTests : SqlMetadataProviderTests
    {
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MYSQL);
        }

        [TestMethod]
        [Ignore]
        public override async Task TestDerivedDatabaseSchemaIsValid()
        {
            ResolverConfig runtimeConfig = _metadataStoreProvider.GetResolvedConfig();
            DatabaseSchema expectedSchema = runtimeConfig.DatabaseSchema;
            DatabaseSchema derivedDatabaseSchema =
                await _sqlMetadataProvider.RefreshDatabaseSchemaWithTablesAsync(_defaultSchemaName);

            foreach ((string tableName, TableDefinition expectedTableDefinition) in expectedSchema.Tables)
            {
                TableDefinition actualTableDefinition;
                Assert.IsTrue(derivedDatabaseSchema.Tables.TryGetValue(tableName, out actualTableDefinition),
                    $"Could not find table definition for table '{tableName}'");

                CollectionAssert.AreEqual(
                    expectedTableDefinition.PrimaryKey,
                    actualTableDefinition.PrimaryKey,
                    $"Did not find the expected primary keys for table {tableName}");

                foreach ((string columnName, ColumnDefinition expectedColumnDefinition) in expectedTableDefinition.Columns)
                {
                    ColumnDefinition actualColumnDefinition;
                    Assert.IsTrue(actualTableDefinition.Columns.TryGetValue(columnName, out actualColumnDefinition),
                        $"Could not find column definition for column '{columnName}' of table '{tableName}'");

                    Assert.AreEqual(expectedColumnDefinition.IsAutoGenerated, actualColumnDefinition.IsAutoGenerated);
                    Assert.AreEqual(expectedColumnDefinition.HasDefault, actualColumnDefinition.HasDefault,
                        $"Expected HasDefault property of column '{columnName}' of table '{tableName}' does not match actual.");
                    Assert.AreEqual(expectedColumnDefinition.IsNullable, actualColumnDefinition.IsNullable,
                        $"Expected IsNullable property of column '{columnName}' of table '{tableName}' does not match actual.");
                }
            }
        }
    }
}
