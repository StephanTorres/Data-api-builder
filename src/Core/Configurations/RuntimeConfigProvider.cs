// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Core.Configurations;

/// <summary>
/// This class is responsible for exposing the runtime config to the rest of the service.
/// The <c>RuntimeConfigProvider</c> won't directly load the config, but will instead rely on the <see cref="FileSystemRuntimeConfigLoader"/> to do so.
/// </summary>
/// <remarks>
/// The <c>RuntimeConfigProvider</c> will maintain internal state of the config, and will only load it once.
///
/// This class should be treated as the owner of the config that is available within the service, and other classes
/// should not load the config directly, or maintain a reference to it, so that we can do hot-reloading by replacing
/// the config that is available from this type.
/// </remarks>
public class RuntimeConfigProvider
{
    public delegate Task<bool> RuntimeConfigLoadedHandler(RuntimeConfigProvider sender, RuntimeConfig config);

    public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; } = new List<RuntimeConfigLoadedHandler>();

    /// <summary>
    /// Indicates whether the config was loaded after the runtime was initialized.
    /// </summary>
    /// <remarks>This is most commonly used when DAB's config is provided via the <c>ConfigurationController</c>, such as when it's a hosted service.</remarks>
    public bool IsLateConfigured { get; set; }

    /// <summary>
    /// The access tokens representing a Managed Identity to connect to the database.
    /// The key is the unique datasource name and the value is the access token.
    /// </summary>
    public Dictionary<string, string?> ManagedIdentityAccessToken { get; private set; } = new Dictionary<string, string?>();

    public RuntimeConfigLoader ConfigLoader { get; private set; }

    private RuntimeConfig? _runtimeConfig;

    public RuntimeConfigProvider(RuntimeConfigLoader runtimeConfigLoader)
    {
        ConfigLoader = runtimeConfigLoader;
    }

    /// <summary>
    /// Return the previous loaded config, or it will attempt to load the config that
    /// is known by the loader.
    /// </summary>
    /// <returns>The RuntimeConfig instance.</returns>
    /// <remark>Dont use this method if environment variable references need to be retained.</remark>
    /// <exception cref="DataApiBuilderException">Thrown when the loader is unable to load an instance of the config from its known location.</exception>
    public RuntimeConfig GetConfig()
    {
        if (_runtimeConfig is not null)
        {
            return _runtimeConfig;
        }

        // While loading the config file, replace all the environment variables with their values.
        if (ConfigLoader.TryLoadKnownConfig(out RuntimeConfig? config, replaceEnvVar: true))
        {
            _runtimeConfig = config;
        }

        if (_runtimeConfig is null)
        {
            throw new DataApiBuilderException(
                message: "Runtime config isn't setup.",
                statusCode: HttpStatusCode.InternalServerError,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
        }

        return _runtimeConfig;
    }

    /// <summary>
    /// Attempt to acquire runtime configuration metadata.
    /// </summary>
    /// <param name="runtimeConfig">Populated runtime configuration, if present.</param>
    /// <returns>True when runtime config is provided, otherwise false.</returns>
    public bool TryGetConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
    {
        if (_runtimeConfig is null)
        {
            if (ConfigLoader.TryLoadKnownConfig(out RuntimeConfig? config, replaceEnvVar: true))
            {
                _runtimeConfig = config;
            }
        }

        runtimeConfig = _runtimeConfig;
        return _runtimeConfig is not null;
    }

    /// <summary>
    /// Attempt to acquire runtime configuration metadata from a previously loaded one.
    /// This method will not load the config if it hasn't been loaded yet.
    /// </summary>
    /// <param name="runtimeConfig">Populated runtime configuration, if present.</param>
    /// <returns>True when runtime config is provided, otherwise false.</returns>
    public bool TryGetLoadedConfig([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
    {
        runtimeConfig = _runtimeConfig;
        return _runtimeConfig is not null;
    }

    /// <summary>
    /// Initialize the runtime configuration provider with the specified configurations.
    /// This initialization method is used when the configuration is sent to the ConfigurationController
    /// in the form of a string instead of reading the configuration from a configuration file.
    /// This method assumes the connection string is provided as part of the configuration.
    /// Initialize the first database within the datasource list.
    /// </summary>
    /// <param name="configuration">The engine configuration.</param>
    /// <param name="schema">The GraphQL Schema. Can be left null for SQL configurations.</param>
    /// <param name="accessToken">The string representation of a managed identity access token</param>
    /// <returns>true if the initialization succeeded, false otherwise.</returns>
    public async Task<bool> Initialize(
        string configuration,
        string? schema,
        string? accessToken)
    {
        if (string.IsNullOrEmpty(configuration))
        {
            throw new ArgumentException($"'{nameof(configuration)}' cannot be null or empty.", nameof(configuration));
        }

        if (RuntimeConfigLoader.TryParseConfig(
                configuration,
                out RuntimeConfig? runtimeConfig,
                replaceEnvVar: true))
        {
            _runtimeConfig = runtimeConfig;

            if (string.IsNullOrEmpty(runtimeConfig.DataSource.ConnectionString))
            {
                throw new ArgumentException($"'{nameof(runtimeConfig.DataSource.ConnectionString)}' cannot be null or empty.", nameof(runtimeConfig.DataSource.ConnectionString));
            }

            if (_runtimeConfig.DataSource.DatabaseType == DatabaseType.CosmosDB_NoSQL)
            {
                _runtimeConfig = HandleCosmosNoSqlConfiguration(schema, _runtimeConfig, _runtimeConfig.DataSource.ConnectionString);
            }

            ManagedIdentityAccessToken[_runtimeConfig.GetDefaultDataSourceName()] = accessToken;
        }

        bool configLoadSucceeded = await InvokeConfigLoadedHandlersAsync();

        IsLateConfigured = true;

        return configLoadSucceeded;
    }

    /// <summary>
    /// Initialize the runtime configuration provider with the specified accessToken.
    /// This initialization method is used to set the access token for the current runtimeConfig.
    /// As opposed to using a json input and regenerating the runtimconfig, it sets the access token for the current runtimeConfig on the provider.
    /// </summary>
    /// <param name="accessToken">The string representation of a managed identity access token</param>
    /// <returns>true if the initialization succeeded, false otherwise.</returns>
    public async Task<bool> InitializeWithAccesstoken(
        string? accessToken,
        string dataSourceName)
    {
        if (TryGetConfig(out RuntimeConfig? runtimeConfig))
        {
            _runtimeConfig = runtimeConfig;
            ManagedIdentityAccessToken[dataSourceName] = accessToken;
        }

        bool configLoadSucceeded = await InvokeConfigLoadedHandlersAsync();

        IsLateConfigured = true;

        return configLoadSucceeded;
    }

    /// <summary>
    /// Initialize the runtime configuration provider with the specified configurations.
    /// This initialization method is used when the configuration is sent to the ConfigurationController
    /// in the form of a string instead of reading the configuration from a configuration file.
    /// </summary>
    /// <param name="configuration">The engine configuration.</param>
    /// <param name="schema">The GraphQL Schema. Can be left null for SQL configurations.</param>
    /// <param name="connectionString">The connection string to the database.</param>
    /// <param name="accessToken">The string representation of a managed identity access token</param>
    /// <returns>true if the initialization succeeded, false otherwise.</returns>
    public async Task<bool> Initialize(string jsonConfig, string? graphQLSchema, string connectionString, string? accessToken)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));
        }

        if (string.IsNullOrEmpty(jsonConfig))
        {
            throw new ArgumentException($"'{nameof(jsonConfig)}' cannot be null or empty.", nameof(jsonConfig));
        }

        IsLateConfigured = true;

        if (RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig? runtimeConfig, replaceEnvVar: true))
        {
            _runtimeConfig = runtimeConfig.DataSource.DatabaseType switch
            {
                DatabaseType.CosmosDB_NoSQL => HandleCosmosNoSqlConfiguration(graphQLSchema, runtimeConfig, connectionString),
                _ => runtimeConfig with { DataSource = runtimeConfig.DataSource with { ConnectionString = connectionString } }
            };
            ManagedIdentityAccessToken[_runtimeConfig.GetDefaultDataSourceName()] = accessToken;
            _runtimeConfig.UpdateDataSourceNameToDataSource(_runtimeConfig.GetDefaultDataSourceName(), _runtimeConfig.DataSource);

            return await InvokeConfigLoadedHandlersAsync();
        }

        return false;
    }

    private async Task<bool> InvokeConfigLoadedHandlersAsync()
    {
        List<Task<bool>> configLoadedTasks = new();
        if (_runtimeConfig is not null)
        {
            foreach (RuntimeConfigLoadedHandler configLoadedHandler in RuntimeConfigLoadedHandlers)
            {
                configLoadedTasks.Add(configLoadedHandler(this, _runtimeConfig));
            }
        }

        bool[] results = await Task.WhenAll(configLoadedTasks);

        // Verify that all tasks succeeded.
        return results.All(x => x);
    }

    private static RuntimeConfig HandleCosmosNoSqlConfiguration(string? schema, RuntimeConfig runtimeConfig, string connectionString, string dataSourceName = "")
    {
        if (string.IsNullOrEmpty(dataSourceName))
        {
            dataSourceName = runtimeConfig.GetDefaultDataSourceName();
        }

        DbConnectionStringBuilder dbConnectionStringBuilder = new()
        {
            ConnectionString = connectionString
        };

        if (string.IsNullOrEmpty(schema))
        {
            throw new ArgumentException($"'{nameof(schema)}' cannot be null or empty.", nameof(schema));
        }

        HyphenatedNamingPolicy namingPolicy = new();

        DataSource dataSource = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName);

        Dictionary<string, JsonElement> options;
        if (dataSource.Options is not null)
        {
            options = new(dataSource.Options)
            {
                // push the "raw" GraphQL schema into the options to pull out later when requested
                { namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.GraphQLSchema)), JsonSerializer.SerializeToElement(schema) }
            };
        }
        else
        {
            throw new ArgumentException($"'{nameof(CosmosDbNoSQLDataSourceOptions)}' cannot be null or empty.", nameof(CosmosDbNoSQLDataSourceOptions));
        }

        // SWA may provide CosmosDB database name in connectionString
        string? database = dbConnectionStringBuilder.ContainsKey("Database") ? (string)dbConnectionStringBuilder["Database"] : null;

        if (database is not null)
        {
            // Add or update the options to contain the parsed database
            options[namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database))] = JsonSerializer.SerializeToElement(database);
        }

        // Update the connection string in the datasource with the one that was provided to the controller
        dataSource = dataSource with { Options = options, ConnectionString = connectionString };

        if (dataSourceName == runtimeConfig.GetDefaultDataSourceName())
        {
            // update default db.
            runtimeConfig = runtimeConfig with { DataSource = dataSource };
        }

        // update dictionary
        runtimeConfig.UpdateDataSourceNameToDataSource(dataSourceName, dataSource);

        return runtimeConfig;
    }
}
