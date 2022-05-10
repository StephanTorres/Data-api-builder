using System;
using Azure.DataGateway.Config;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class encapsulates methods to validate the runtime config file.
    /// </summary>
    public class RuntimeConfigValidator : IConfigValidator
    {
        private readonly RuntimeConfig? _runtimeConfig;

        public RuntimeConfigValidator(
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath)
        {
            _runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue;
        }

        public RuntimeConfigValidator(RuntimeConfig config)
        {
            _runtimeConfig = config;
        }

        public void ValidateConfig()
        {
            if (_runtimeConfig is null)
            {
                throw new ArgumentNullException("hawaii-config",
                    "The runtime configuration value has not been set yet.");
            }

            if (string.IsNullOrWhiteSpace(_runtimeConfig.DatabaseType.ToString()))
            {
                throw new NotSupportedException("The database-type should be provided" +
                     " with the runtime config.");
            }

            if (string.IsNullOrWhiteSpace(_runtimeConfig.ConnectionString))
            {
                throw new NotSupportedException($"The Connection String should be provided.");
            }

            if (string.IsNullOrEmpty(_runtimeConfig.DataSource.ResolverConfigFile))
            {
                throw new NotSupportedException("The resolver-config-file should be provided" +
                    " with the runtime config.");
            }

            ValidateAuthenticationConfig();
        }

        private void ValidateAuthenticationConfig()
        {
            bool isAudienceSet =
                _runtimeConfig!.AuthNConfig != null &&
                _runtimeConfig!.AuthNConfig.Jwt != null &&
                !string.IsNullOrEmpty(_runtimeConfig!.AuthNConfig.Jwt.Audience);
            bool isIssuerSet =
                _runtimeConfig!.AuthNConfig != null &&
                _runtimeConfig!.AuthNConfig.Jwt != null &&
                !string.IsNullOrEmpty(_runtimeConfig!.AuthNConfig.Jwt.Issuer);
            if (!_runtimeConfig!.IsEasyAuthAuthenticationProvider() && (!isAudienceSet || !isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer must be set" +
                    " when not using EasyAuth.");
            }

            if (_runtimeConfig!.IsEasyAuthAuthenticationProvider() && (isAudienceSet || isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer should not be set" +
                    " and are not used with EasyAuth.");
            }
        }
    }
}
