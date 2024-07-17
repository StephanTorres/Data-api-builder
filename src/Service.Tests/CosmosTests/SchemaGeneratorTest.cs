// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using Azure.DataApiBuilder.Core.Generator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SchemaGeneratorTest
    {
        [TestMethod]
        [DataRow("CosmosTests/TestData/CosmosData", "CosmosTests/TestData/GeneratedGqlSchema")]
        public void TestSchemaGenerator(string jsonFilePath, string gqlFilePath)
        {
            string json = Regex.Replace(File.ReadAllText($"{jsonFilePath}/EmulatorData.json", Encoding.UTF8), @"\s+", string.Empty);
            List<JObject> jsonArray = new () { JsonConvert.DeserializeObject<JObject>(json) };

            string actualSchema = SchemaGenerator.Generate(jsonArray, "containerName");
            string expectedSchema = File.ReadAllText($"{gqlFilePath}/EmulatorData.gql");

            AreEqualAfterCleanup(expectedSchema, actualSchema);
        }

        [TestMethod]
        [DataRow("CosmosTests/TestData/CosmosData/MultiItems", "CosmosTests/TestData/GeneratedGqlSchema")]
        public void TestSchemaGeneratorUsingMultipleJson(string jsonFilePath, string gqlFilePath)
        {
            List<JObject> jArray = new ();

            string[] successPayloadFiles = Directory.GetFiles(jsonFilePath, "*.json");
            foreach (string payloadFile in successPayloadFiles)
            {
                string json = Regex.Replace(File.ReadAllText(payloadFile, Encoding.UTF8), @"\s+", string.Empty);
                jArray.Add(JsonConvert.DeserializeObject<JObject>(json));
            }

            string actualSchema = SchemaGenerator.Generate(jArray, "containerName");
            string expectedSchema = File.ReadAllText($"{gqlFilePath}/MultiItems.gql");

            AreEqualAfterCleanup(expectedSchema, actualSchema);
        }
        [TestMethod]
        public void TestMixDataJsonObject()
        {
            List<JObject> jsonArray = new() {
                JObject.Parse(@"{
                  ""id"": 12345,
                  ""name"": ""Widget"",
                  ""price"": 19.99,
                  ""inStock"": true,
                  ""tags"": [ ""gadget"", ""tool"", ""home"" ],
                  ""dimensions"": {
                    ""length"": 10.5,
                    ""width"": 7.25,
                    ""height"": 3.0
                  },
                  ""manufacturedDate"": ""2021-08-15T08:00:00Z"",
                  ""relatedProducts"": [
                    23456,
                    34567,
                    45678
                  ]
                }
                ")};

            string gqlSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model {
  id : ID!,
  name : String!,
  price : Float!,
  inStock : Boolean!,
  tags : [String!],
  dimensions : Dimensions!,
  manufacturedDate : String!,
  relatedProducts : [Int!]
}
type Dimensions {
  length : Float!,
  width : Float!,
  height : Float!
}";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestComplexJsonObject()
        {
            List<JObject> jsonArray = new() {
                JObject.Parse(@"{
                  ""name"": ""John Doe"",
                  ""age"": 30,
                  ""address"": {
                    ""street"": ""123 Main St"",
                    ""city"": ""Anytown"",
                    ""state"": ""CA"",
                    ""zip"": ""12345"",
                    ""coordinates"": {
                      ""latitude"": 34.0522,
                      ""longitude"": -118.2437
                    }
                  },
                  ""emails"": [
                    ""john.doe@example.com"",
                    ""john.doe@work.com""
                  ],
                  ""phoneNumbers"": [
                    {
                      ""type"": ""home"",
                      ""number"": ""555-555-5555""
                    },
                    {
                      ""type"": ""work"",
                      ""number"": ""555-555-5556""
                    }
                  ]
                }")};

            string gqlSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model {
                                          name : String!,
                                          age : Int!,
                                          address : Address!,
                                          emails : [String!],
                                          phoneNumbers : [PhoneNumber!]
                                        }
                                        type Address {
                                          street : String!,
                                          city : String!,
                                          state : String!,
                                          zip : String!,
                                          coordinates : Coordinates!
                                        }
                                        type Coordinates {
                                          latitude : Float!,
                                          longitude : Float!
                                        }
                                        type PhoneNumber {
                                          type : String!,
                                          number : String!
                                        }";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestMixedJsonArray()
        {
            List<JObject> jsonArray = new() {
                JObject.Parse(@"{ ""name"": ""John"", ""age"": 30, ""isStudent"": false, ""birthDate"": ""1980-01-01T00:00:00Z"" }"),
                JObject.Parse(@"{ ""email"": ""john@example.com"", ""phone"": ""123-456-7890"" }")};

            string gqlSchema = SchemaGenerator.Generate(jsonArray, "containerName");

            string expectedSchema = @"type ContainerName @model {
              name: String,
              age: Int,
              isStudent: Boolean,
              birthDate: String,
              email: String,
              phone: String
            }";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        [TestMethod]
        public void TestEmptyJsonArray()
        {
            List<JObject> jsonArray = new ();
            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Generate(jsonArray, "containerName"));
        }

        [TestMethod]
        public void TestArrayContainingNullObject()
        {
            List<JObject> jsonArray = new();
            jsonArray.Add(null);

            Assert.ThrowsException<InvalidOperationException>(() => SchemaGenerator.Generate(jsonArray, "containerName"));
        }

        [TestMethod]
        public void TestJsonArrayWithNullElement()
        {
            JArray jsonArray = JArray.Parse(@"[{ ""name"": ""John"", ""age"": null }]");

            string gqlSchema = SchemaGenerator.Generate(jsonArray.Select(item => (JObject)item).ToList(), "containerName");

            string expectedSchema = @"type ContainerName @model {
              name: String!,
              age: String!
            }";

            AreEqualAfterCleanup(expectedSchema, gqlSchema);
        }

        public static string RemoveSpacesAndNewLinesRegex(string input)
        {
            return Regex.Replace(input, @"\s+", "");
        }

        public static void AreEqualAfterCleanup(string actual, string expected)
        {
           Assert.AreEqual(RemoveSpacesAndNewLinesRegex(expected), RemoveSpacesAndNewLinesRegex(actual));
        }
    }
}
