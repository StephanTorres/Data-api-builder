using Azure.DataGateway.Config;
using CommandLine;

namespace Hawaii.Cli.Models
{
    /// <summary>
    /// Common options for all the commands
    /// </summary>
    public class Options
    {
        public Options(string name)
        {
            this.Name = name;
        }

        [Option('n', "name", Default = (string)RuntimeConfigPath.CONFIGFILE_NAME, Required = false, HelpText = "Config file name")]
        public string Name { get; }
    }

    /// <summary>
    /// Init command options
    /// </summary>
    [Verb("init", isDefault: false, HelpText = "Initialize configuration file.", Hidden = false)]
    public class InitOptions : Options
    {
        public InitOptions(
            DatabaseType databaseType,
            string connectionString,
            string? cosmosDatabase,
            string? cosmosContainer,
            string? graphQLSchemaPath,
            HostModeType hostMode,
            string name)
            : base(name)
        {
            this.DatabaseType = databaseType;
            this.ConnectionString = connectionString;
            this.CosmosDatabase = cosmosDatabase;
            this.CosmosContainer = cosmosContainer;
            this.GraphQLSchemaPath = graphQLSchemaPath;
            this.HostMode = hostMode;
        }

        [Option("database-type", Required = true, HelpText = "Type of database to connect. Supported values: mssql, cosmos, mysql, postgresql")]
        public DatabaseType DatabaseType { get; }

        [Option("connection-string", Required = true, HelpText = "Connection details to connect to the database.")]
        public string ConnectionString { get; }

        [Option("cosmos-database", Required = false, HelpText = "Database name for Cosmos DB.")]
        public string? CosmosDatabase { get; }

        [Option("cosmos-container", Required = false, HelpText = "Container name for Cosmos DB.")]
        public string? CosmosContainer { get; }

        [Option("graphql-schema", Required = false, HelpText = "GraphQL schema Path.")]
        public string? GraphQLSchemaPath { get; }

        [Option("host-mode", Default = HostModeType.Production, Required = false, HelpText = "Specify the Host mode - Development or Production")]
        public HostModeType HostMode { get; }
    }

    /// <summary>
    /// Commond options for entitiy manipulation.
    /// </summary>
    public class EntityOptions : Options
    {
        public EntityOptions(
            string entity,
            string? restRoute,
            string? graphQLType,
            string? fieldsToInclude,
            string? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string name)
            : base(name)
        {
            this.Entity = entity;
            this.RestRoute = restRoute;
            this.GraphQLType = graphQLType;
            this.FieldsToInclude = fieldsToInclude;
            this.FieldsToExclude = fieldsToExclude;
            this.PolicyRequest = policyRequest;
            this.PolicyDatabase = policyDatabase;
        }

        [Value(0, MetaName = "Entity", Required = true, HelpText = "Name of the entity.")]
        public string Entity { get; }

        [Option("rest", Required = false, HelpText = "Route for rest api.")]
        public string? RestRoute { get; }

        [Option("graphql", Required = false, HelpText = "Type of graphQL.")]
        public string? GraphQLType { get; }

        [Option("fields.include", Required = false, HelpText = "Fields that are allowed access to permission.")]
        public string? FieldsToInclude { get; }

        [Option("fields.exclude", Required = false, HelpText = "Fields that are excluded from the action lists.")]
        public string? FieldsToExclude { get; }

        [Option("policy-request", Required = false, HelpText = "Specify the rule to be checked before sending any request to the database.")]
        public string? PolicyRequest { get; }

        [Option("policy-database", Required = false, HelpText = "Specify an OData style filter rule that will be injected in the query sent to the database.")]
        public string? PolicyDatabase { get; }
    }

    /// <summary>
    /// Add command options
    /// </summary>
    [Verb("add", isDefault: false, HelpText = "Add a new entity to the configuration file.", Hidden = false)]
    public class AddOptions : EntityOptions
    {
        public AddOptions(
            string source,
            string permissions,
            string entity,
            string? restRoute,
            string? graphQLType,
            string? fieldsToInclude,
            string? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string name)
            : base(entity,
                  restRoute,
                  graphQLType,
                  fieldsToInclude,
                  fieldsToExclude,
                  policyRequest,
                  policyDatabase,
                  name)
        {
            this.Source = source;
            this.Permissions = permissions;
        }

        [Option('s', "source", Required = true, HelpText = "Name of the source table or container.")]
        public string Source { get; }

        [Option("permissions", Required = true, HelpText = "Permissions required to access the source table or container.")]
        public string Permissions { get; }
    }

    /// <summary>
    /// Update command options
    /// </summary>
    [Verb("update", isDefault: false, HelpText = "Update an existing entity in the configuration file.", Hidden = false)]
    public class UpdateOptions : EntityOptions
    {
        public UpdateOptions(
            string? source,
            string? permissions,
            string entity,
            string? restRoute,
            string? graphQLType,
            string? fieldsToInclude,
            string? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string? relationship,
            string? cardinality,
            string? targetEntity,
            string? linkingObject,
            string? linkingSourceFields,
            string? linkingTargetFields,
            string? mappingFields,
            string name)
            : base(entity,
                  restRoute,
                  graphQLType,
                  fieldsToInclude,
                  fieldsToExclude,
                  policyRequest,
                  policyDatabase,
                  name)
        {
            this.Source = source;
            this.Permissions = permissions;
            this.Relationship = relationship;
            this.Cardinality = cardinality;
            this.TargetEntity = targetEntity;
            this.LinkingObject = linkingObject;
            this.LinkingSourceFields = linkingSourceFields;
            this.LinkingTargetFields = linkingTargetFields;
            this.MappingFields = mappingFields;
        }

        [Option('s', "source", Required = false, HelpText = "Name of the source table or container.")]
        public string? Source { get; }

        [Option("permissions", Required = false, HelpText = "Permissions required to access the source table or container.")]
        public string? Permissions { get; }

        [Option("relationship", Required = false, HelpText = "Specify relationship between two entities.")]
        public string? Relationship { get; }

        [Option("cardinality", Required = false, HelpText = "Specify cardinality between two entities.")]
        public string? Cardinality { get; }

        [Option("target.entity", Required = false, HelpText = "Another exposed entity to which the source entity relates to.")]
        public string? TargetEntity { get; }

        [Option("linking.object", Required = false, HelpText = "Database object that is used to support an M:N relationship.")]
        public string? LinkingObject { get; }

        [Option("linking.source.fields", Required = false, HelpText = "Database fields in the linking object to connect to the related item in the source entity.")]
        public string? LinkingSourceFields { get; }

        [Option("linking.target.fields", Required = false, HelpText = "Database fields in the linking object to connect to the related item in the target entity.")]
        public string? LinkingTargetFields { get; }

        [Option("mapping.fields", Required = false, HelpText = "Specify fields to be used for mapping the entities.")]
        public string? MappingFields { get; }
    }
}
