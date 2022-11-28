using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Tests.Authorization;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using HotChocolate;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;
using Npgsql;
using static Azure.DataApiBuilder.Config.RuntimeConfigPath;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class ConfigurationTests
    {
        private const string ASP_NET_CORE_ENVIRONMENT_VAR_NAME = "ASPNETCORE_ENVIRONMENT";
        private const string COSMOS_ENVIRONMENT = TestCategory.COSMOS;
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;
        private const string MYSQL_ENVIRONMENT = TestCategory.MYSQL;
        private const string POSTGRESQL_ENVIRONMENT = TestCategory.POSTGRESQL;
        private const string POST_STARTUP_CONFIG_ENTITY = "Book";
        private const string POST_STARTUP_CONFIG_ENTITY_SOURCE = "books";
        private const string POST_STARTUP_CONFIG_ROLE = "PostStartupConfigRole";
        private const int RETRY_COUNT = 5;
        private const int RETRY_WAIT_SECONDS = 1;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            TestContext.Properties.Add(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, Environment.GetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME));
            TestContext.Properties.Add(RUNTIME_ENVIRONMENT_VAR_NAME, Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME));
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            if (File.Exists($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.Test.overrides{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.Test.overrides{CONFIG_EXTENSION}");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.HostTest.overrides{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.HostTest.overrides{CONFIG_EXTENSION}");
            }
        }

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, (string)TestContext.Properties[ASP_NET_CORE_ENVIRONMENT_VAR_NAME]);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, (string)TestContext.Properties[RUNTIME_ENVIRONMENT_VAR_NAME]);
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "");
            Environment.SetEnvironmentVariable($"{ENVIRONMENT_PREFIX}{nameof(RuntimeConfigPath.CONNSTRING)}", "");
        }

        /// <summary>
        /// When updating config during runtime is possible, then For invalid config the Application continues to
        /// accept request with status code of 503.
        /// But if invalid config is provided during startup, ApplicationException is thrown
        /// and application exits.
        /// </summary>
        [DataTestMethod]
        [DataRow(new string[] { }, true, DisplayName = "No config returns 503 - config file flag absent")]
        [DataRow(new string[] { "--ConfigFileName=" }, true, DisplayName = "No config returns 503 - empty config file option")]
        [DataRow(new string[] { }, false, DisplayName = "Throws Application exception")]
        [TestMethod("Validates that queries before runtime is configured returns a 503 in hosting scenario whereas an application exception when run through CLI")]
        public async Task TestNoConfigReturnsServiceUnavailable(
            string[] args,
            bool isUpdateableRuntimeConfig)
        {
            TestServer server;

            try
            {
                if (isUpdateableRuntimeConfig)
                {
                    server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(args));
                }
                else
                {
                    server = new(Program.CreateWebHostBuilder(args));
                }

                HttpClient httpClient = server.CreateClient();
                HttpResponseMessage result = await httpClient.GetAsync("/graphql");
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
            }
            catch (Exception e)
            {
                Assert.IsFalse(isUpdateableRuntimeConfig);
                Assert.AreEqual(typeof(ApplicationException), e.GetType());
                Assert.AreEqual(
                    $"Could not initialize the engine with the runtime config file: {RuntimeConfigPath.DefaultName}",
                    e.Message);
            }
        }

        /// <summary>
        /// Checks correct serialization and deserialization of Source Type from 
        /// Enum to String and vice-versa.
        /// Consider both cases for source as an object and as a string
        /// </summary>
        [DataTestMethod]
        [DataRow(true, SourceType.StoredProcedure, "stored-procedure", DisplayName = "source is a stored-procedure")]
        [DataRow(true, SourceType.Table, "table", DisplayName = "source is a table")]
        [DataRow(true, SourceType.View, "view", DisplayName = "source is a view")]
        [DataRow(false, null, null, DisplayName = "source is just string")]
        public void TestCorrectSerializationOfSourceObject(
            bool isDatabaseObjectSource,
            SourceType sourceObjectType,
            string sourceTypeName)
        {
            object entitySource;
            if (isDatabaseObjectSource)
            {
                entitySource = new DatabaseObjectSource(
                    Type: sourceObjectType,
                    Name: "sourceName",
                    Parameters: null,
                    KeyFields: null
                );
            }
            else
            {
                entitySource = "sourceName";
            }

            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: "MyEntity",
                entitySource: entitySource,
                roleName: "Anonymous",
                operation: Operation.All,
                includedCols: null,
                excludedCols: null,
                databasePolicy: null
                );

            string runtimeConfigJson = JsonSerializer.Serialize<RuntimeConfig>(runtimeConfig);

            if (isDatabaseObjectSource)
            {
                Assert.IsTrue(runtimeConfigJson.Contains(sourceTypeName));
            }

            Mock<ILogger> logger = new();
            Assert.IsTrue(RuntimeConfig.TryGetDeserializedRuntimeConfig(
                runtimeConfigJson,
                out RuntimeConfig deserializedRuntimeConfig,
                logger.Object));

            Assert.IsTrue(deserializedRuntimeConfig.Entities.ContainsKey("MyEntity"));
            deserializedRuntimeConfig.Entities["MyEntity"].TryPopulateSourceFields();
            Assert.AreEqual("sourceName", deserializedRuntimeConfig.Entities["MyEntity"].SourceName);

            JsonElement sourceJson = (JsonElement)deserializedRuntimeConfig.Entities["MyEntity"].Source;
            if (isDatabaseObjectSource)
            {
                Assert.AreEqual(JsonValueKind.Object, sourceJson.ValueKind);
                Assert.AreEqual(sourceObjectType, deserializedRuntimeConfig.Entities["MyEntity"].ObjectType);
            }
            else
            {
                Assert.AreEqual(JsonValueKind.String, sourceJson.ValueKind);
                Assert.AreEqual("sourceName", deserializedRuntimeConfig.Entities["MyEntity"].Source.ToString());
            }
        }

        [TestMethod("Validates that once the configuration is set, the config controller isn't reachable."), TestCategory(TestCategory.COSMOS)]
        public async Task TestConflictAlreadySetConfiguration()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            _ = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            ValidateCosmosDbSetup(server);

            HttpResponseMessage result = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates that the config controller returns a conflict when using local configuration."), TestCategory(TestCategory.COSMOS)]
        public async Task TestConflictLocalConfiguration()
        {
            Environment.SetEnvironmentVariable
                (ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ValidateCosmosDbSetup(server);

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage result =
                await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates setting the configuration at runtime."), TestCategory(TestCategory.COSMOS)]
        public async Task TestSettingConfigurations()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage postResult =
                await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
        }

        /// <summary>
        /// Tests that sending configuration to the DAB engine post-startup will properly hydrate
        /// the AuthorizationResolver by:
        /// 1. Validate that pre-configuration hydration requests result in 503 Service Unavailable
        /// 2. Validate that custom configuration hydration succeeds.
        /// 3. Validate that request to protected entity without role membership triggers Authorization Resolver
        /// to reject the request with HTTP 403 Forbidden.
        /// 4. Validate that request to protected entity with required role membership passes authorization requirements
        /// and succeeds with HTTP 200 OK.
        /// Note: This test is database engine agnostic, though requires denoting a database environment to fetch a usable
        /// connection string to complete the test. Most applicable to CI/CD test execution.
        /// </summary>
        [TestCategory(TestCategory.MSSQL)]
        [TestMethod("Validates setting the AuthN/Z configuration post-startup during runtime.")]
        public async Task TestSqlSettingPostStartupConfigurations()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);

            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            RuntimeConfig configuration = AuthorizationHelpers.InitRuntimeConfig(
                entityName: POST_STARTUP_CONFIG_ENTITY,
                entitySource: POST_STARTUP_CONFIG_ENTITY_SOURCE,
                roleName: POST_STARTUP_CONFIG_ROLE,
                operation: Operation.Read,
                includedCols: new HashSet<string>() { "*" });

            ConfigurationPostParameters config = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration);

            HttpResponseMessage preConfigHydradtionResult =
                await httpClient.GetAsync($"/{POST_STARTUP_CONFIG_ENTITY}");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, preConfigHydradtionResult.StatusCode);

            HttpStatusCode responseCode = await HydratePostStartupConfiguration(httpClient, config);

            // When the authorization resolver is properly configured, authorization will have failed
            // because no auth headers are present.
            Assert.AreEqual(
                expected: HttpStatusCode.Forbidden,
                actual: responseCode,
                message: "Configuration not yet hydrated after retry attempts..");

            // Sends a GET request to a protected entity which requires a specific role to access.
            // Authorization will pass because proper auth headers are present.
            HttpRequestMessage message = new(method: HttpMethod.Get, requestUri: $"api/{POST_STARTUP_CONFIG_ENTITY}");
            string swaTokenPayload = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(
                addAuthenticated: true,
                specificRole: POST_STARTUP_CONFIG_ROLE);
            message.Headers.Add(AuthenticationConfig.CLIENT_PRINCIPAL_HEADER, swaTokenPayload);
            message.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, POST_STARTUP_CONFIG_ROLE);
            HttpResponseMessage authorizedResponse = await httpClient.SendAsync(message);
            Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);
        }

        [TestMethod("Validates that local cosmos settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.COSMOS)]
        public void TestLoadingLocalCosmosSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates access token is correctly loaded when Account Key is not present for Cosmos."), TestCategory(TestCategory.COSMOS)]
        public async Task TestLoadingAccessTokenForCosmosClient()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ConfigurationPostParameters config = GetCosmosConfigurationParametersWithAccessToken();

            HttpResponseMessage authorizedResponse = await httpClient.PostAsync("/configuration", JsonContent.Create(config));

            Assert.AreEqual(expected: HttpStatusCode.OK, actual: authorizedResponse.StatusCode);
            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Client);
        }

        [TestMethod("Validates that local MsSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MSSQL)]
        public void TestLoadingLocalMsSqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MsSqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<SqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MsSqlMetadataProvider));
        }

        [TestMethod("Validates that local PostgreSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.POSTGRESQL)]
        public void TestLoadingLocalPostgresSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, POSTGRESQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(PostgresQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<NpgsqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(PostgreSqlMetadataProvider));
        }

        [TestMethod("Validates that local MySql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MYSQL)]
        public void TestLoadingLocalMySqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MYSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MySqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<MySqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MySqlMetadataProvider));
        }

        [TestMethod("Validates that trying to override configs that are already set fail."), TestCategory(TestCategory.COSMOS)]
        public async Task TestOverridingLocalSettingsFails()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, postResult.StatusCode);
        }

        [TestMethod("Validates that setting the configuration at runtime will instantiate the proper classes."), TestCategory(TestCategory.COSMOS)]
        public async Task TestSettingConfigurationCreatesCorrectClasses()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            ValidateCosmosDbSetup(server);
            RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();

            Assert.IsNotNull(configProvider, "Configuration Provider shouldn't be null after setting the configuration at runtime.");
            Assert.IsNotNull(configProvider.GetRuntimeConfiguration(), "Runtime Configuration shouldn't be null after setting the configuration at runtime.");
            RuntimeConfig configuration;
            bool isConfigSet = configProvider.TryGetRuntimeConfiguration(out configuration);
            Assert.IsNotNull(configuration, "TryGetRuntimeConfiguration should set the config in the out parameter.");
            Assert.IsTrue(isConfigSet, "TryGetRuntimeConfiguration should return true when the config is set.");

            Assert.AreEqual(DatabaseType.cosmos, configuration.DatabaseType, "Expected cosmos database type after configuring the runtime with cosmos settings.");
            Assert.AreEqual(config.Schema, configuration.CosmosDb.GraphQLSchema, "Expected the schema in the configuration to match the one sent to the configuration endpoint.");
            Assert.AreEqual(config.ConnectionString, configuration.ConnectionString, "Expected the connection string in the configuration to match the one sent to the configuration endpoint.");
        }

        [TestMethod("Validates that an exception is thrown if there's a null model in filter parser.")]
        public void VerifyExceptionOnNullModelinFilterParser()
        {
            ODataParser parser = new();
            try
            {
                // FilterParser has no model so we expect exception
                parser.GetFilterClause(filterQueryString: string.Empty, resourcePath: string.Empty);
                Assert.Fail();
            }
            catch (DataApiBuilderException exception)
            {
                Assert.AreEqual("The runtime has not been initialized with an Edm model.", exception.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
            }
        }

        /// <summary>
        /// This test reads the dab-config.MsSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of MsSql config file succeeds."), TestCategory(TestCategory.MSSQL)]
        public void TestReadingRuntimeConfigForMsSql()
        {
            ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigPath.CONFIGFILE_NAME}.{MSSQL_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.MySql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of MySql config file succeeds."), TestCategory(TestCategory.MYSQL)]
        public void TestReadingRuntimeConfigForMySql()
        {
            ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigPath.CONFIGFILE_NAME}.{MYSQL_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.PostgreSql.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of PostgreSql config file succeeds."), TestCategory(TestCategory.POSTGRESQL)]
        public void TestReadingRuntimeConfigForPostgreSql()
        {
            ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigPath.CONFIGFILE_NAME}.{POSTGRESQL_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// This test reads the dab-config.Cosmos.json file and validates that the
        /// deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of the cosmos config file succeeds."), TestCategory(TestCategory.COSMOS)]
        public void TestReadingRuntimeConfigForCosmos()
        {
            ConfigFileDeserializationValidationHelper(File.ReadAllText($"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"));
        }

        /// <summary>
        /// Helper method to validate the deserialization of the "entities" section of the config file
        /// This is used in unit tests that validate the deserialiation of the config files
        /// </summary>
        /// <param name="runtimeConfig"></param>
        private static void ConfigFileDeserializationValidationHelper(string jsonString)
        {
            Mock<ILogger> logger = new();
            RuntimeConfig.TryGetDeserializedRuntimeConfig(jsonString, out RuntimeConfig runtimeConfig, logger.Object);
            Assert.IsNotNull(runtimeConfig.Schema);
            Assert.IsInstanceOfType(runtimeConfig.DataSource, typeof(DataSource));
            Assert.IsTrue(runtimeConfig.DataSource.CosmosDbNoSql == null
                || runtimeConfig.DataSource.CosmosDbNoSql.GetType() == typeof(CosmosDbOptions));
            Assert.IsTrue(runtimeConfig.DataSource.MsSql == null
                || runtimeConfig.DataSource.MsSql.GetType() == typeof(MsSqlOptions));
            Assert.IsTrue(runtimeConfig.DataSource.PostgreSql == null
                || runtimeConfig.DataSource.PostgreSql.GetType() == typeof(PostgreSqlOptions));
            Assert.IsTrue(runtimeConfig.DataSource.MySql == null
                || runtimeConfig.DataSource.MySql.GetType() == typeof(MySqlOptions));

            foreach (Entity entity in runtimeConfig.Entities.Values)
            {
                Assert.IsTrue(((JsonElement)entity.Source).ValueKind == JsonValueKind.String
                    || ((JsonElement)entity.Source).ValueKind == JsonValueKind.Object);

                Assert.IsTrue(entity.Rest == null
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.True
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.False
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.Object);
                if (entity.Rest != null
                    && ((JsonElement)entity.Rest).ValueKind == JsonValueKind.Object)
                {
                    RestEntitySettings rest =
                        ((JsonElement)entity.Rest).Deserialize<RestEntitySettings>(RuntimeConfig.SerializerOptions);
                    Assert.IsTrue(((JsonElement)rest.Path).ValueKind == JsonValueKind.String);
                }

                Assert.IsInstanceOfType(entity.Permissions, typeof(PermissionSetting[]));
                foreach (PermissionSetting permission in entity.Permissions)
                {
                    foreach (object operation in permission.Operations)
                    {
                        HashSet<Operation> allowedActions =
                            new() { Operation.All, Operation.Create, Operation.Read,
                                Operation.Update, Operation.Delete };
                        Assert.IsTrue(((JsonElement)operation).ValueKind == JsonValueKind.String ||
                            ((JsonElement)operation).ValueKind == JsonValueKind.Object);
                        if (((JsonElement)operation).ValueKind == JsonValueKind.Object)
                        {
                            Config.PermissionOperation configOperation =
                                ((JsonElement)operation).Deserialize<Config.PermissionOperation>(RuntimeConfig.SerializerOptions);
                            Assert.IsTrue(allowedActions.Contains(configOperation.Name));
                            Assert.IsTrue(configOperation.Policy == null
                                || configOperation.Policy.GetType() == typeof(Policy));
                            Assert.IsTrue(configOperation.Fields == null
                                || configOperation.Fields.GetType() == typeof(Field));
                        }
                        else
                        {
                            Operation name = AuthorizationResolver.WILDCARD.Equals(operation.ToString()) ? Operation.All : ((JsonElement)operation).Deserialize<Operation>(RuntimeConfig.SerializerOptions);
                            Assert.IsTrue(allowedActions.Contains(name));
                        }
                    }
                }

                Assert.IsTrue(entity.Relationships == null ||
                    entity.Relationships.GetType()
                        == typeof(Dictionary<string, Relationship>));
                Assert.IsTrue(entity.Mappings == null ||
                    entity.Mappings.GetType()
                        == typeof(Dictionary<string, string>));
            }
        }

        /// <summary>
        /// This function verifies command line configuration provider takes higher
        /// precendence than default configuration file dab-config.json
        /// </summary>
        [TestMethod("Validates command line configuration provider."), TestCategory(TestCategory.COSMOS)]
        public void TestCommandLineConfigurationProvider()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            string[] args = new[]
            {
                $"--ConfigFileName={RuntimeConfigPath.CONFIGFILE_NAME}." +
                $"{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
            };

            TestServer server = new(Program.CreateWebHostBuilder(args));

            ValidateCosmosDbSetup(server);
        }

        /// <summary>
        /// This function verifies the environment variable DAB_ENVIRONMENT
        /// takes precendence than ASPNETCORE_ENVIRONMENT for the configuration file.
        /// </summary>
        [TestMethod("Validates precedence is given to DAB_ENVIRONMENT environment variable name."), TestCategory(TestCategory.COSMOS)]
        public void TestRuntimeEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable(
                ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);

            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates the runtime configuration file."), TestCategory(TestCategory.MSSQL)]
        public void TestConfigIsValid()
        {
            RuntimeConfigPath configPath =
                TestHelper.GetRuntimeConfigPath(MSSQL_ENVIRONMENT);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configPath);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            IConfigValidator configValidator =
                new RuntimeConfigValidator(
                    configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object);

            configValidator.ValidateConfig();
        }

        /// <summary>
        /// Set the connection string to an invalid value and expect the service to be unavailable
        /// since without this env var, it would be available - guaranteeing this env variable
        /// has highest precedence irrespective of what the connection string is in the config file.
        /// Verifying the Exception thrown.
        /// </summary>
        [TestMethod("Validates that environment variable DAB_CONNSTRING has highest precedence."), TestCategory(TestCategory.COSMOS)]
        public void TestConnectionStringEnvVarHasHighestPrecedence()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                $"{RuntimeConfigPath.ENVIRONMENT_PREFIX}{nameof(RuntimeConfigPath.CONNSTRING)}",
                "Invalid Connection String");

            try
            {
                TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
                _ = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
                Assert.Fail($"{RuntimeConfigPath.ENVIRONMENT_PREFIX}{nameof(RuntimeConfigPath.CONNSTRING)} is not given highest precedence");
            }
            catch (Exception e)
            {
                Assert.AreEqual(typeof(ApplicationException), e.GetType());
                Assert.AreEqual(
                    $"Could not initialize the engine with the runtime config file: " +
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}",
                    e.Message);
            }
        }

        /// <summary>
        /// Test to verify the precedence logic for config file based on Environment variables.
        /// </summary>
        [DataTestMethod]
        [DataRow("HostTest", "Test", false, $"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}", DisplayName = "hosting and dab environment set, without considering overrides.")]
        [DataRow("HostTest", "", false, $"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, without considering overrides.")]
        [DataRow("", "Test1", false, $"{CONFIGFILE_NAME}.Test1{CONFIG_EXTENSION}", DisplayName = "only dab environment set, without considering overrides.")]
        [DataRow("", "Test2", true, $"{CONFIGFILE_NAME}.Test2.overrides{CONFIG_EXTENSION}", DisplayName = "only dab environment set, considering overrides.")]
        [DataRow("HostTest1", "", true, $"{CONFIGFILE_NAME}.HostTest1.overrides{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, considering overrides.")]
        public void TestGetConfigFileNameForEnvironment(
            string hostingEnvironmentValue,
            string environmentValue,
            bool considerOverrides,
            string expectedRuntimeConfigFile)
        {
            if (!File.Exists(expectedRuntimeConfigFile))
            {
                File.Create(expectedRuntimeConfigFile);
            }

            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, hostingEnvironmentValue);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
            string actualRuntimeConfigFile = GetFileNameForEnvironment(hostingEnvironmentValue, considerOverrides);
            Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
        }

        /// <summary>
        /// Test different graphql endpoints in different host modes
        /// when accessed interactively via browser.
        /// </summary>
        /// <param name="endpoint">The endpoint route</param>
        /// <param name="hostModeType">The mode in which the service is executing.</param>
        /// <param name="expectedStatusCode">Expected Status Code.</param>
        /// <param name="expectedContent">The expected phrase in the response body.</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/graphql/", HostModeType.Development, HttpStatusCode.OK, "Banana Cake Pop",
            DisplayName = "GraphQL endpoint with no query in development mode.")]
        [DataRow("/graphql", HostModeType.Production, HttpStatusCode.BadRequest,
            "Either the parameter query or the parameter id has to be set",
            DisplayName = "GraphQL endpoint with no query in production mode.")]
        [DataRow("/graphql/ui", HostModeType.Development, HttpStatusCode.NotFound,
            DisplayName = "Default BananaCakePop in development mode.")]
        [DataRow("/graphql/ui", HostModeType.Production, HttpStatusCode.NotFound,
            DisplayName = "Default BananaCakePop in production mode.")]
        [DataRow("/graphql?query={book_by_pk(id: 1){title}}",
            HostModeType.Development, HttpStatusCode.Moved,
            DisplayName = "GraphQL endpoint with query in development mode.")]
        [DataRow("/graphql?query={book_by_pk(id: 1){title}}",
            HostModeType.Production, HttpStatusCode.OK, "data",
            DisplayName = "GraphQL endpoint with query in production mode.")]
        [DataRow(RestController.REDIRECTED_ROUTE, HostModeType.Development, HttpStatusCode.BadRequest,
            "GraphQL request redirected to favicon.ico.",
            DisplayName = "Redirected endpoint in development mode.")]
        [DataRow(RestController.REDIRECTED_ROUTE, HostModeType.Production, HttpStatusCode.BadRequest,
            "GraphQL request redirected to favicon.ico.",
            DisplayName = "Redirected endpoint in production mode.")]
        public async Task TestInteractiveGraphQLEndpoints(
            string endpoint,
            HostModeType hostModeType,
            HttpStatusCode expectedStatusCode,
            string expectedContent = "")
        {
            const string CUSTOM_CONFIG = "custom-config.json";
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(MSSQL_ENVIRONMENT);
            RuntimeConfig config = configProvider.GetRuntimeConfiguration();
            HostGlobalSettings customHostGlobalSettings = config.HostGlobalSettings with { Mode = hostModeType };
            JsonElement serializedCustomHostGlobalSettings =
                JsonSerializer.SerializeToElement(customHostGlobalSettings, RuntimeConfig.SerializerOptions);
            Dictionary<GlobalSettingsType, object> customRuntimeSettings = new(config.RuntimeSettings);
            customRuntimeSettings.Remove(GlobalSettingsType.Host);
            customRuntimeSettings.Add(GlobalSettingsType.Host, serializedCustomHostGlobalSettings);
            RuntimeConfig configWithCustomHostMode =
                config with { RuntimeSettings = customRuntimeSettings };
            File.WriteAllText(
                CUSTOM_CONFIG,
                JsonSerializer.Serialize(configWithCustomHostMode, RuntimeConfig.SerializerOptions));
            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            TestServer server = new(Program.CreateWebHostBuilder(args));

            HttpClient client = server.CreateClient();
            HttpRequestMessage request = new(HttpMethod.Get, endpoint);

            // Adding the following headers simulates an interactive browser request.
            request.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/105.0.0.0 Safari/537.36");
            request.Headers.Add("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");

            HttpResponseMessage response = await client.SendAsync(request);
            Assert.AreEqual(expectedStatusCode, response.StatusCode);
            string actualBody = await response.Content.ReadAsStringAsync();
            Assert.IsTrue(actualBody.Contains(expectedContent));
        }

        /// <summary>
        /// Tests that the custom path rewriting middleware properly rewrites the
        /// first segment of a path (/segment1/.../segmentN) when the segment matches
        /// the custom configured GraphQLEndpoint.
        /// Note: The GraphQL service is always internally mapped to /graphql
        /// </summary>
        /// <param name="graphQLConfiguredPath">The custom configured GraphQL path in configuration</param>
        /// <param name="requestPath">The path used in the web request executed in the test.</param>
        /// <param name="expectedStatusCode">Expected Http success/error code</param>
        [DataTestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow("/graphql", "/gql", HttpStatusCode.BadRequest, DisplayName = "Request to non-configured graphQL endpoint is handled by REST controller.")]
        [DataRow("/graphql", "/graphql", HttpStatusCode.OK, DisplayName = "Request to configured default GraphQL endpoint succeeds, path not rewritten.")]
        [DataRow("/gql", "/gql/additionalURLsegment", HttpStatusCode.OK, DisplayName = "GraphQL request path (with extra segments) rewritten to match internally set GraphQL endpoint /graphql.")]
        [DataRow("/gql", "/gql", HttpStatusCode.OK, DisplayName = "GraphQL request path rewritten to match internally set GraphQL endpoint /graphql.")]
        [DataRow("/gql", "/api/book", HttpStatusCode.NotFound, DisplayName = "Non-GraphQL request's path is not rewritten and is handled by REST controller.")]
        [DataRow("/gql", "/graphql", HttpStatusCode.NotFound, DisplayName = "Requests to default/internally set graphQL endpoint fail when configured endpoint differs.")]
        public async Task TestPathRewriteMiddlewareForGraphQL(
            string graphQLConfiguredPath,
            string requestPath,
            HttpStatusCode expectedStatusCode)
        {
            Dictionary<GlobalSettingsType, object> settings = new()
            {
                { GlobalSettingsType.GraphQL, JsonSerializer.SerializeToElement(new GraphQLGlobalSettings(){ Path = graphQLConfiguredPath, AllowIntrospection = true }) }
            };

            DataSource dataSource = new(DatabaseType.mssql)
            {
                ConnectionString = GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL)
            };

            RuntimeConfig configuration = InitMinimalRuntimeConfig(globalSettings: settings, dataSource: dataSource);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(
                CUSTOM_CONFIG,
                JsonSerializer.Serialize(configuration, RuntimeConfig.SerializerOptions));

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                string query = @"{
                    book_by_pk(id: 1) {
                       id,
                       title,
                       publisher_id
                    }
                }";

                object payload = new { query };

                HttpRequestMessage request = new(HttpMethod.Post, requestPath)
                {
                    Content = JsonContent.Create(payload)
                };

                HttpResponseMessage response = await client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                Assert.AreEqual(expectedStatusCode, response.StatusCode);
            }
        }

        /// <summary>
        /// Integration test that validates schema introspection requests fail
        /// when allow-introspection is false in the runtime configuration.
        /// TestCategory is required for CI/CD pipeline to inject a connection string.
        /// </summary>
        /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/6b2cfc94695cb65e2f68f5d8deb576e48397a98a/src/HotChocolate/Core/src/Abstractions/ErrorCodes.cs#L287"/>
        [TestCategory(TestCategory.MSSQL)]
        [DataTestMethod]
        [DataRow(false, true, "Introspection is not allowed for the current request.", DisplayName = "Disabled introspection returns GraphQL error.")]
        [DataRow(true, false, null, DisplayName = "Enabled introspection does not return introspection forbidden error.")]
        public async Task TestSchemaIntrospectionQuery(bool enableIntrospection, bool expectError, string? errorMessage)
        {
            Dictionary<GlobalSettingsType, object> settings = new()
            {
                { GlobalSettingsType.GraphQL, JsonSerializer.SerializeToElement(new GraphQLGlobalSettings(){ AllowIntrospection = enableIntrospection }) }
            };

            DataSource dataSource = new(DatabaseType.mssql)
            {
                ConnectionString = GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL)
            };

            RuntimeConfig configuration = InitMinimalRuntimeConfig(globalSettings: settings, dataSource: dataSource);
            const string CUSTOM_CONFIG = "custom-config.json";
            File.WriteAllText(
                CUSTOM_CONFIG,
                JsonSerializer.Serialize(configuration, RuntimeConfig.SerializerOptions));

            string[] args = new[]
            {
                    $"--ConfigFileName={CUSTOM_CONFIG}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                await ExecuteGraphQLIntrospectionQueries(server, client, expectError);
            }

            // Instantiate new server with no runtime config for post-startup configuration hydration tests.
            using (TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>())))
            using (HttpClient client = server.CreateClient())
            {
                ConfigurationPostParameters config = GetPostStartupConfigParams(MSSQL_ENVIRONMENT, configuration);
                HttpStatusCode responseCode = await HydratePostStartupConfiguration(client, config);

                Assert.AreEqual(expected: HttpStatusCode.OK, actual: responseCode, message: "Configuration hydration failed.");

                await ExecuteGraphQLIntrospectionQueries(server, client, expectError);
            }
        }

        /// <summary>
        /// Validates that schema introspection requests fail when allow-introspection is false in the runtime configuration.
        /// </summary>
        /// <seealso cref="https://github.com/ChilliCream/hotchocolate/blob/6b2cfc94695cb65e2f68f5d8deb576e48397a98a/src/HotChocolate/Core/src/Abstractions/ErrorCodes.cs#L287"/>
        private static async Task ExecuteGraphQLIntrospectionQueries(TestServer server, HttpClient client, bool expectError)
        {
            string graphQLQueryName = "__schema";
            string graphQLQuery = @"{
                __schema {
                    types {
                        name
                    }
                }
            }";

            string expectedErrorMessageFragment = "Introspection is not allowed for the current request.";

            try
            {
                RuntimeConfigProvider configProvider = server.Services.GetRequiredService<RuntimeConfigProvider>();

                JsonElement actual = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                    client,
                    configProvider,
                    query: graphQLQuery,
                    queryName: graphQLQueryName,
                    variables: null,
                    clientRoleHeader: null
                    );

                if (expectError)
                {
                    SqlTestHelper.TestForErrorInGraphQLResponse(
                        response: actual.ToString(),
                        message: expectedErrorMessageFragment,
                        statusCode: ErrorCodes.Validation.IntrospectionNotAllowed
                    );
                }
            }
            catch (Exception ex)
            {
                // ExecuteGraphQLRequestAsync will raise an exception when no "data" key
                // exists in the GraphQL JSON response.
                Assert.Fail(message: "No schema metadata in GraphQL response." + ex.Message);
            }
        }

        private static ConfigurationPostParameters GetCosmosConfigurationParameters()
        {
            string cosmosFile = $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}";
            return new(
                File.ReadAllText(cosmosFile),
                File.ReadAllText("schema.gql"),
                "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                AccessToken: null);
        }

        /// <summary>
        /// With an invalid access token, when a new instance of CosmosClient is created with that token, it won't throw an exception.
        /// But when a graphql request is coming in, that's when it throws an 401 exception.
        /// To prevent this, CosmosClientProvider parses the token and retrieves the "exp" property from the token,
        /// if it's not valid, then we will throw an exception from our code before it initiating a client.
        /// Uses a valid fake JWT access token for testing purposes.
        /// </summary>
        /// <returns>ConfigurationPostParameters object</returns>
        private static ConfigurationPostParameters GetCosmosConfigurationParametersWithAccessToken()
        {
            string cosmosFile = $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}";
            return new(
                File.ReadAllText(cosmosFile),
                File.ReadAllText("schema.gql"),
                "AccountEndpoint=https://localhost:8081/;",
                AccessToken: "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiZXhwIjoxMjMzNDQ1Nn0.1cdRZfqwndt67f-sHKgOfEgTfO9xDyGFl6_d-RRyf4U");
        }

        /// <summary>
        /// Helper used to create the post-startup configuration payload sent to configuration controller.
        /// Adds entity used to hydrate authorization resolver post-startup and validate that hydration succeeds.
        /// Additional pre-processing performed acquire database connection string from a local file.
        /// </summary>
        /// <returns>ConfigurationPostParameters object.</returns>
        private static ConfigurationPostParameters GetPostStartupConfigParams(string environment, RuntimeConfig runtimeConfig)
        {
            string connectionString = GetConnectionStringFromEnvironmentConfig(environment);

            string serializedConfiguration = JsonSerializer.Serialize(runtimeConfig);

            return new ConfigurationPostParameters(
                Configuration: serializedConfiguration,
                Schema: null,
                ConnectionString: connectionString,
                AccessToken: null);
        }

        /// <summary>
        /// Hydrates configuration after engine has started and triggers service instantiation
        /// by executing HTTP requests against the engine until a non-503 error is received.
        /// </summary>
        /// <param name="httpClient">Client used for request execution.</param>
        /// <param name="config">Post-startup configuration</param>
        /// <returns>ServiceUnavailable if service is not successfully hydrated with config</returns>
        private static async Task<HttpStatusCode> HydratePostStartupConfiguration(HttpClient httpClient, ConfigurationPostParameters config)
        {
            // Hydrate configuration post-startup
            HttpResponseMessage postResult =
                await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            // Retry request RETRY_COUNT times in 1 second increments to allow required services
            // time to instantiate and hydrate permissions.
            int retryCount = RETRY_COUNT;
            HttpStatusCode responseCode = HttpStatusCode.ServiceUnavailable;
            while (retryCount > 0)
            {
                // Spot test authorization resolver utilization to ensure configuration is used.
                HttpResponseMessage postConfigHydrationResult =
                    await httpClient.GetAsync($"api/{POST_STARTUP_CONFIG_ENTITY}");
                responseCode = postConfigHydrationResult.StatusCode;

                if (postConfigHydrationResult.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    retryCount--;
                    Thread.Sleep(TimeSpan.FromSeconds(RETRY_WAIT_SECONDS));
                    continue;
                }

                break;
            }

            return responseCode;
        }

        /// <summary>
        /// Instantiate minimal runtime config with custom global settings.
        /// </summary>
        /// <param name="globalSettings">Globla settings config.</param>
        /// <param name="dataSource">DataSource to pull connectionstring required for engine start.</param>
        /// <returns></returns>
        public static RuntimeConfig InitMinimalRuntimeConfig(Dictionary<GlobalSettingsType, object> globalSettings, DataSource dataSource)
        {
            Entity sampleEntity = new(
                Source: JsonSerializer.SerializeToElement("books"),
                Rest: null,
                GraphQL: JsonSerializer.SerializeToElement(new GraphQLEntitySettings(Type: new SingularPlural(Singular: "book", Plural: "books"))),
                Permissions: new PermissionSetting[] { GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null
                );

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", sampleEntity }
            };

            RuntimeConfig runtimeConfig = new(
                Schema: "IntegrationTestMinimalSchema",
                DataSource: dataSource,
                RuntimeSettings: globalSettings,
                Entities: entityMap
                );

            runtimeConfig.DetermineGlobalSettings();
            return runtimeConfig;
        }

        /// <summary>
        /// Gets PermissionSetting object allowed to perform all actions.
        /// </summary>
        /// <param name="roleName">Name of role to assign to permission</param>
        /// <returns>PermissionSetting</returns>
        public static PermissionSetting GetMinimalPermissionConfig(string roleName)
        {
            PermissionOperation actionForRole = new(
                Name: Operation.All,
                Fields: null,
                Policy: new(request: null, database: null)
                );

            return new PermissionSetting(
                role: roleName,
                operations: new object[] { JsonSerializer.SerializeToElement(actionForRole) }
                );
        }

        /// <summary>
        /// Reads configuration file for defined environment to acquire the connection string.
        /// CI/CD Pipelines and local environments may not have connection string set as environment variable.
        /// </summary>
        /// <param name="environment">Environment such as TestCategory.MSSQL</param>
        /// <returns>Connection string</returns>
        public static string GetConnectionStringFromEnvironmentConfig(string environment)
        {
            string sqlFile = GetFileNameForEnvironment(environment, considerOverrides: true);
            string configPayload = File.ReadAllText(sqlFile);

            Mock<ILogger> logger = new();
            RuntimeConfig.TryGetDeserializedRuntimeConfig(configPayload, out RuntimeConfig runtimeConfig, logger.Object);

            return runtimeConfig.ConnectionString;
        }

        private static void ValidateCosmosDbSetup(TestServer server)
        {
            object metadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(metadataProvider, typeof(CosmosSqlMetadataProvider));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(CosmosQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(CosmosMutationEngine));

            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Client);
        }

        private bool HandleException<T>(Exception e) where T : Exception
        {
            if (e is AggregateException aggregateException)
            {
                aggregateException.Handle(HandleException<T>);
                return true;
            }
            else if (e is T)
            {
                return true;
            }

            return false;
        }
    }
}
