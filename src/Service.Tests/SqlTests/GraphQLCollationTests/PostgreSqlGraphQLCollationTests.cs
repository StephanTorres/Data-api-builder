// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLCollationTests : GraphQLCollationTestBase
    {
        //Collation setting for database
        private const string DEFAULT_COLLATION = "pg_catalog.\"default\"";
        private const string CASE_INSENSITIVE_COLLATION = "pg_catalog.\"en-US-u-va-posix-x-icu\"";

        /// <summary>
        /// Set the database engine for tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        #region Tests
        /// <summary>
        /// Postgre Capitalization Collation Tests
        /// </summary>
        [DataTestMethod]
        [DataRow("comics", "title", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT title FROM comics ORDER BY title asc) as table0")]
        [DataRow("authors", "name", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT name FROM authors ORDER BY name asc) as table0")]
        [DataRow("fungi", "habitat", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT habitat FROM fungi ORDER BY habitat asc) as table0")]
        public async Task PostgreCapitalizationResultQuery(string type, string item, string dbQuery)
        {
            string defaultCollationQuery = PostgreCollationQuery(type, item, DEFAULT_COLLATION);
            string newCollationQuery = PostgreCollationQuery(type, item, CASE_INSENSITIVE_COLLATION);
            await CapitalizationResultQuery(type, item, dbQuery, defaultCollationQuery, newCollationQuery);
        }

        /// <summary>
        /// Creates collation query for a specific column on a table in the database for Postgre
        /// </summary>
        private static string PostgreCollationQuery(string table, string column, string newCollation)
        {
            string dbQuery = @"
                ALTER TABLE " + table + @"
                ALTER COLUMN " + column + @" TYPE text
                COLLATE " + newCollation;
            return dbQuery;
        }
        #endregion
    }
}
