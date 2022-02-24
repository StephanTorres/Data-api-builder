using System.Collections.Generic;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Tests.CosmosTests;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass]
    public class SqlConfigValidatorTests
    {
        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        [Ignore("Ignoring test until ID field support is included")]
        public void CanCreateFieldWithIDType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Person {
                  id: ID!
                }

                type Query {
                    person(id: ID!): Person
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Person", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "id", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Int,
                                    IsAutoGenerated = true,
                                    IsNullable = false } } },
                            PrimaryKey = new List<string> { "id" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Person", new Models.GraphQLType (Table: "Person", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithStringType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Person {
                  name: String!
                }

                type Query {
                    person(name: String!): Person
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Person", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "name", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Varchar,
                                    IsNullable = false } } },
                            PrimaryKey = new List<string> { "name" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Person", new Models.GraphQLType (Table: "Person", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithIntType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Foo {
                  bar: Int!
                }

                type Query {
                    foo(bar: Int!): Foo
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Foo", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "bar", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Int,
                                    IsNullable = false } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Foo", new Models.GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithFloatType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Foo {
                  bar: Float!
                }

                type Query {
                    foo(bar: Float!): Foo
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Foo", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "bar", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Float,
                                    IsNullable = false } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Foo", new Models.GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithFloatTypeAndDoubleColumn()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Foo {
                  bar: Float!
                }

                type Query {
                    foo(bar: Float!): Foo
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Foo", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "bar", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Double,
                                    IsNullable = false } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Foo", new Models.GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithBooleanType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Foo {
                  bar: Boolean!
                }

                type Query {
                    foo(bar: Boolean!): Foo
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Foo", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "bar", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Bit,
                                    IsNullable = false } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Foo", new Models.GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableIDType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Person {
                  id: ID
                }

                type Query {
                    person(id: ID): Person
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Person", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "id", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Int,
                                    IsAutoGenerated = true,
                                    IsNullable = true } } },
                            PrimaryKey = new List<string> { "id" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Person", new Models.GraphQLType (Table: "Person", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableStringType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Person {
                  name: String
                }

                type Query {
                    person(name: String): Person
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Person", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "name", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Varchar,
                                    IsNullable = true } } },
                            PrimaryKey = new List<string> { "name" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Person", new Models.GraphQLType (Table: "Person", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableIntType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Foo {
                  bar: Int
                }

                type Query {
                    foo(bar: Int): Foo
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Foo", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "bar", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Int,
                                    IsNullable = true } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Foo", new Models.GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableFloatType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Foo {
                  bar: Float
                }

                type Query {
                    foo(bar: Float): Foo
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Foo", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "bar", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Float,
                                    IsNullable = true } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Foo", new Models.GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableBooleanType()
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = @"
                type Foo {
                  bar: Boolean
                }

                type Query {
                    foo(bar: Boolean): Foo
                }
                ",
                DatabaseSchema = new Models.DatabaseSchema
                {
                    Tables = new Dictionary<string, Models.TableDefinition> {
                        { "Foo", new Models.TableDefinition {
                            Columns = new Dictionary<string, Models.ColumnDefinition> {
                                { "bar", new Models.ColumnDefinition {
                                    Type = Models.ColumnType.Bit,
                                    IsNullable = true } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, Models.GraphQLType> {
                    { "Foo", new Models.GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }
    }
}
