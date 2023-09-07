// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Services.MetadataProviders
{
    /// <inheritdoc />
    public class MetadataProviderFactory : IMetadataProviderFactory
    {
        private readonly IDictionary<string, ISqlMetadataProvider> _metadataProviders;

        public MetadataProviderFactory(RuntimeConfigProvider runtimeConfigProvider, IQueryManagerFactory engineFactory, ILogger<ISqlMetadataProvider> logger)
        {
            _metadataProviders = new Dictionary<string, ISqlMetadataProvider>();
            foreach ((string dataSourceName, DataSource dataSource) in runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator())
            {
                ISqlMetadataProvider metadataProvider = dataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => null!,
                    DatabaseType.MSSQL => new MsSqlMetadataProvider(runtimeConfigProvider, engineFactory, logger, dataSourceName),
                    DatabaseType.PostgreSQL => new PostgreSqlMetadataProvider(runtimeConfigProvider, engineFactory, logger, dataSourceName),
                    DatabaseType.MySQL => new MySqlMetadataProvider(runtimeConfigProvider, engineFactory, logger, dataSourceName),
                    _ => throw new NotSupportedException(dataSource.DatabaseTypeNotSupportedMessage),
                };

                _metadataProviders.TryAdd(dataSourceName, metadataProvider);
            }
        }

        /// <inheritdoc />
        public ISqlMetadataProvider GetMetadataProvider(string dataSourceName)
        {
            if (!_metadataProviders.ContainsKey(dataSourceName))
            {
                throw new DataApiBuilderException($"{nameof(dataSourceName)}:{dataSourceName} could not be found within the config", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return _metadataProviders[dataSourceName];
        }

        /// <inheritdoc />
        public async Task InitializeAsync()
        {
            foreach ((_, ISqlMetadataProvider provider) in _metadataProviders)
            {
                await provider.InitializeAsync();
            }
        }

        public IEnumerable<ISqlMetadataProvider> ListMetadataProviders()
        {
            return _metadataProviders.Values;
        }   
    }
}
