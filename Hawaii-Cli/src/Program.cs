using CommandLine;
using Hawaii.Cli.Models;

namespace Hawaii.Cli
{
    /// <summary>
    /// Main class for CLI
    /// </summary>
    class Program
    {
        /// <summary>
        /// Main CLI entry point
        /// </summary>
        /// <param name="args">CLI arguments</param>
        /// <returns>0 on success, -1 on failure.</returns>
        public static int Main(string[] args)
        {
            ParserResult<object>? result = Parser.Default.ParseArguments<InitOptions, AddOptions, UpdateOptions>(args)
                .WithParsed<InitOptions>(options =>
                {
                    bool isSuccess = ConfigGenerator.GenerateConfig(options);
                    if (isSuccess)
                    {
                        Console.WriteLine($"Config generated with file name: {options.Name}, database type: {options.DatabaseType}, and connectionString: {options.ConnectionString}");
                        Console.WriteLine($"SUGGESTION: Use 'hawaii add <options>' to add new entities in your config.");
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: Could not generate config with file name: {options.Name}, database type: {options.DatabaseType}, and connectionString: {options.ConnectionString}");
                    }
                })
                .WithParsed<AddOptions>(options =>
                {
                    bool isSuccess = ConfigGenerator.AddEntitiesToConfig(options);
                    if (isSuccess)
                    {
                        Console.WriteLine($"Added new entity:{options.Entity} with source: {options.Source} to config: {options.Name} with permissions: {options.Permissions}.");
                        Console.WriteLine($"SUGGESTION: Use 'hawaii update <options>' to update any entities in your config.");
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: Could not add entity:{options.Entity} source: {options.Source} to config: {options.Name} with permissions: {options.Permissions}.");
                    }
                })
                .WithParsed<UpdateOptions>(options =>
                {
                    bool isSuccess = ConfigGenerator.UpdateEntity(options);

                    if (isSuccess)
                    {
                        Console.WriteLine($"Updated the entity:{options.Entity} in the config.");
                    }
                    else
                    {
                        Console.WriteLine($"Could not update the entity:{options.Entity}.");
                    }
                });

            return result is Parsed<object> ? 0 : -1;
        }
    }
}
