using System;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// The different type of databases this app supports.
    /// </summary>
    public enum DatabaseType
    {
        Cosmos,
        MsSql,
        PostgreSql,
        MySql
    }
    /// <summary>
    /// Data gateway configuration.
    /// </summary>
    public class DataGatewayConfig
    {
        /*
         * This is an example of the configuration format
         *
         "DataGatewayConfig": {
            "DatabaseType": "",
            "ResolverConfigFile" : ""
            "DatabaseConnection": {
                "ServerEndpointUrl": "",
                "AuthorizationKey": "",
                "Server": "",
                "Database": "",
                "Container": "",
                "ConnectionString": ""
              },
            "JwtAuth": {
                "Type":"",
                "Audience":"",
                "Issuer":""
            }
         */
        public DatabaseType DatabaseType { get; set; }

        // This should be renamed to databaseConnection but need to coordiate with moderakh on CI configuration.
        public DatabaseConnectionConfig DatabaseConnection { get; set; } = null!;
        public string ResolverConfigFile { get; set; } = null!;
        public JwtAuthProviderConfig JwtAuth { get; set; } = null!;
    }

    /// <summary>
    /// Database connection configuration.
    /// </summary>
    public class DatabaseConnectionConfig
    {
        public string ServerEndpointUrl { get; set; } = null!;
        public string AuthorizationKey { get; set; } = null!;
        public string Server { get; set; } = null!;
        public string Database { get; set; } = null!;
        public string Container { get; set; } = null!;
        public string ConnectionString { get; set; } = null!;
    }

    /// <summary>
    /// Jwt Auth Provider Config
    /// </summary>
    public class JwtAuthProviderConfig
    {
        //Type is Identity Provider such as AzureAD. If set to EasyAuth, no Audience, or issuer expected.
        public string Type { get; set; } = null!;
        public string Audience { get; set; } = null!;
        public string Issuer { get; set; } = null!;
    }

    /// <summary>
    /// Post configuration processing for DataGatewayConfig.
    /// We check for database connection options. If the user does not provide connection string,
    /// we build a connection string from other connection settings and finally set the ConnectionString setting.
    ///
    /// This inteface is called before IValidateOptions. Hence, we need to do some validation here.
    /// </summary>
    public class DataGatewayConfigPostConfiguration : IPostConfigureOptions<DataGatewayConfig>
    {
        public void PostConfigure(string name, DataGatewayConfig options)
        {
            bool connStringProvided = !string.IsNullOrEmpty(options.DatabaseConnection.ConnectionString);
            bool serverProvided = !string.IsNullOrEmpty(options.DatabaseConnection.Server);
            bool dbProvided = !string.IsNullOrEmpty(options.DatabaseConnection.Database);
            if (!connStringProvided && !serverProvided && !dbProvided)
            {
                throw new NotSupportedException("Either Server and Database or ConnectionString need to be provided");
            }
            else if (connStringProvided && (serverProvided || dbProvided))
            {
                throw new NotSupportedException("Either Server and Database or ConnectionString need to be provided, not both");
            }

            if (string.IsNullOrWhiteSpace(options.DatabaseConnection.ConnectionString))
            {
                if ((!serverProvided && dbProvided) || (serverProvided && !dbProvided))
                {
                    throw new NotSupportedException("Both Server and Database need to be provided");
                }

                SqlConnectionStringBuilder builder = new()
                {
                    InitialCatalog = options.DatabaseConnection.Database,
                    DataSource = options.DatabaseConnection.Server,
                };

                builder.IntegratedSecurity = true;
                options.DatabaseConnection.ConnectionString = builder.ToString();
            }
        }
    }

    /// <summary>
    /// Validate config.
    /// This happens after post configuration.
    /// </summary>
    public class DataGatewayConfigValidation : IValidateOptions<DataGatewayConfig>
    {
        public ValidateOptionsResult Validate(string name, DataGatewayConfig options)
        {
            return string.IsNullOrWhiteSpace(options.DatabaseConnection.ConnectionString)
                ? ValidateOptionsResult.Fail("Invalid connection string.")
                : ValidateOptionsResult.Success;
        }
    }
}
