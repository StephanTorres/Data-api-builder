using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class OtherRestApiTests : RestApiTestBase
    {
        #region RestApiTestBase Overrides

        public override string GetQuery(string key)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
            // Setup REST Components
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object,
                _authorizationResolver,
                _runtimeConfigProvider);
            _restController = new RestController(_restService,
                                                 _restControllerLogger);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        /// <summary>
        /// Tests the REST Api for the correct error condition format when
        /// a DataApiBuilderException is thrown
        /// </summary>
        [TestMethod]
        public async Task RestDataApiBuilderExceptionErrorConditionFormat()
        {
            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: "?$select=id,content",
                entity: _integrationEntityName,
                sqlQuery: string.Empty,
                exception: true,
                expectedErrorMessage: "Invalid field to be returned requested: content",
                expectedStatusCode: HttpStatusCode.BadRequest
            );
        }

        /// <summary>
        /// This test verifies that when we have an unsupported opration,
        /// in this case a none operation, that we return the correct error
        /// response.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task HandleAndExecuteUnsupportedOperationUnitTestAsync()
        {
            string expectedBody = string.Empty;
            // Setup params to invoke function with
            // Must use valid entity name
            string path = "api";
            string entityName = "Book";
            HttpMethod method = HttpMethod.Head;

            // Since primarykey route and querystring are empty for this test, end point would only comprise
            // of path and entity name.
            string restEndPoint = path + "/" + entityName;

            HttpRequestMessage request = new(method, restEndPoint);
            // need header to instantiate identity in controller
            request.Headers.Add("x-ms-client-principal", Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"hello\":\"world\"}")));

            // Send request to the engine.
            HttpResponseMessage response = await HttpClient.SendAsync(request);

            // Read response as string.
            string responseBody = await response.Content.ReadAsStringAsync();

            // Assert that expectedBody and responseBody are the same.
            Assert.AreEqual(expectedBody, responseBody);
        }

        #region Private helpers

        /// <summary>
        /// Helper function uses reflection to invoke
        /// private methods from outside class.
        /// Expects async method returning Task.
        /// </summary>
        class PrivateObject
        {
            private readonly object _classToInvoke;
            public PrivateObject(object classToInvoke)
            {
                _classToInvoke = classToInvoke;
            }

            public Task<IActionResult> Invoke(string privateMethodName, params object[] privateMethodArgs)
            {
                MethodInfo methodInfo = _classToInvoke.GetType().GetMethod(privateMethodName, BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (methodInfo is null)
                {
                    throw new System.Exception($"{privateMethodName} not found in class '{_classToInvoke.GetType()}'");
                }

                return (Task<IActionResult>)methodInfo.Invoke(_classToInvoke, privateMethodArgs);
            }
        }

        #endregion
    }
}
