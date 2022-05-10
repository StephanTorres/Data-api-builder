using System;
using System.IO;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    class TestHelper
    {
        public static readonly string DB_NAME = "graphqlTestDb";
        private static Lazy<IOptions<DataGatewayConfig>> _dataGatewayConfig = new(() => TestHelper.LoadConfig());

        private static IOptions<DataGatewayConfig> LoadConfig()
        {
            DataGatewayConfig dataGatewayConfig = new();
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile($"appsettings.Cosmos.json")
                .Build();

            config.Bind(nameof(DataGatewayConfig), dataGatewayConfig);

            return Options.Create(dataGatewayConfig);
        }

        public static IOptions<DataGatewayConfig> DataGatewayConfig
        {
            get { return _dataGatewayConfig.Value; }
        }

        public static IOptionsMonitor<DataGatewayConfig> DataGatewayConfigMonitor
        {
            get
            {
                return Mock.Of<IOptionsMonitor<DataGatewayConfig>>(_ => _.CurrentValue == DataGatewayConfig.Value);
            }
        }

        public static object GetItem(string id)
        {
            return new
            {
                id = id,
                name = "test name",
                myProp = "a value",
                myIntProp = 4,
                myBooleanProp = true,
                anotherPojo = new
                {
                    anotherProp = "myname",
                    anotherIntProp = 55,
                    person = new
                    {
                        firstName = "A Person",
                        lastName = "the last name",
                        zipCode = 784298
                    }
                },
                character = new
                {
                    id = id,
                    name = "planet character",
                    type = "Mars",
                    homePlanet = 1,
                    primaryFunction = "test function"
                }
            };
        }
    }
}
