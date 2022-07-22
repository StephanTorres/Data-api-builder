using System.Threading.Tasks;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Authorization.GraphQL
{
    /// <summary>
    /// Tests Database Authorization Policies applied to GraphQL Queries
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class GraphQLDeleteMutationDatabasePolicyTests : SqlTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Tests Authenticated GraphQL Delete Mutation which triggers
        /// policy processing. Tests deleteBook with policy that
        /// allows/prevents operation.
        /// - Operation allowed: confirm record deleted.
        /// - Operation forbidden: confirm record not deleted.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation_Policy()
        {
            string dbQuery = @"
                SELECT TOP 1
                [table0].[id] AS [id],
                [table0].[title] AS [title]
                FROM [dbo].[books] AS [table0] 
                WHERE ([table0].[id] = 9 and [table0].[title] = 'Policy-Test-01') 
                ORDER BY [table0].[id] ASC 
                FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER";

            string graphQLMutationName = "deleteBook";
            string graphQLMutation = @"mutation {
                deleteBook(id: 9)
                {
                    title,
                    publisher_id
                }
            }
            ";

            // Delete Book Policy: @item.id ne 9
            // Test that the delete fails due to restrictive delete policy.
            // Confirm that records are not deleted.
            await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_07");

            string expected = await GetDatabaseResultAsync(dbQuery);
            Assert.IsNotNull(expected, message: "Expected result was null, erroneous delete occurred.");

            // Delete Book Policy: @item.id eq 9
            // Test that the delete is successful when policy allows operation.
            // Confirm that record is deleted.
            await ExecuteGraphQLRequestAsync(
                graphQLMutation,
                graphQLMutationName,
                isAuthenticated: true,
                clientRoleHeader: "policy_tester_08");

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.IsNull(dbResponse, message: "Expected result was not null, delete operation failed.");
        }
    }
}
