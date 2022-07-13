using System;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    public class CosmosTestHelper: TestHelper
    {
        public static readonly string DB_NAME = "graphqlTestDb";
        private static Lazy<RuntimeConfigPath>
            _runtimeConfigPath = new(() => GetRuntimeConfigPath(TestCategory.COSMOS));

        public static RuntimeConfigPath ConfigPath
        {
            get
            {
                return _runtimeConfigPath.Value;
            }
        }

        public static RuntimeConfigProvider ConfigProvider
        {
            get
            {
                return GetRuntimeConfigProvider(ConfigPath);
            }
        }

        public static RuntimeConfig Config
        {
            get
            {
                return GetRuntimeConfig(ConfigProvider);
            }
        }

        public static object GetItem(string id, string name = null, int numericVal = 4)
        {
            return new
            {
                id = id,
                name = string.IsNullOrEmpty(name) ? "test name" : name,
                dimension = "space",
                age = numericVal,
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
