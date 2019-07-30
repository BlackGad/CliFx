﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CliFx.Exceptions;
using CliFx.Internal;
using CliFx.Models;
using CliFx.Services;

namespace CliFx
{
    public partial class CliApplication : ICliApplication
    {
        private readonly ApplicationMetadata _applicationMetadata;
        private readonly IReadOnlyList<Type> _commandTypes;

        private readonly IConsole _console;
        private readonly ICommandInputParser _commandInputParser;
        private readonly ICommandSchemaResolver _commandSchemaResolver;
        private readonly ICommandFactory _commandFactory;
        private readonly ICommandInitializer _commandInitializer;
        private readonly ICommandHelpTextRenderer _commandHelpTextRenderer;

        public CliApplication(ApplicationMetadata applicationMetadata, IReadOnlyList<Type> commandTypes,
            IConsole console, ICommandInputParser commandInputParser, ICommandSchemaResolver commandSchemaResolver,
            ICommandFactory commandFactory, ICommandInitializer commandInitializer, ICommandHelpTextRenderer commandHelpTextRenderer)
        {
            _applicationMetadata = applicationMetadata;
            _commandTypes = commandTypes;

            _console = console;
            _commandInputParser = commandInputParser;
            _commandSchemaResolver = commandSchemaResolver;
            _commandFactory = commandFactory;
            _commandInitializer = commandInitializer;
            _commandHelpTextRenderer = commandHelpTextRenderer;
        }

        public CliApplication(ApplicationMetadata applicationMetadata, IReadOnlyList<Type> commandTypes)
            : this(applicationMetadata, commandTypes,
                new SystemConsole(), new CommandInputParser(), new CommandSchemaResolver(),
                new CommandFactory(), new CommandInitializer(), new CommandHelpTextRenderer())
        {
        }

        public CliApplication(IReadOnlyList<Type> commandTypes)
            : this(GetDefaultApplicationMetadata(), commandTypes)
        {
        }

        public CliApplication()
            : this(GetDefaultCommandTypes())
        {
        }

        private IReadOnlyList<CommandSchema> GetAvailableCommandSchemas() =>
            _commandTypes.Select(_commandSchemaResolver.GetCommandSchema).ToArray();

        private CommandSchema GetMatchingCommandSchema(IReadOnlyList<CommandSchema> availableCommandSchemas, string commandName) =>
            availableCommandSchemas.FirstOrDefault(c => string.Equals(c.Name, commandName, StringComparison.OrdinalIgnoreCase));

        private bool IsHelpRequested(CommandInput commandInput)
        {
            var firstOptionAlias = commandInput.Options.FirstOrDefault()?.Alias;

            return string.Equals(firstOptionAlias, "help", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(firstOptionAlias, "h", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(firstOptionAlias, "?", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsVersionRequested(CommandInput commandInput)
        {
            var firstOptionAlias = commandInput.Options.FirstOrDefault()?.Alias;

            return string.Equals(firstOptionAlias, "version", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<int> RunAsync(IReadOnlyList<string> commandLineArguments)
        {
            try
            {
                var commandInput = _commandInputParser.ParseInput(commandLineArguments);

                var availableCommandSchemas = GetAvailableCommandSchemas();
                var matchingCommandSchema = GetMatchingCommandSchema(availableCommandSchemas, commandInput.CommandName);

                // Fail if there are no commands defined
                if (!availableCommandSchemas.Any())
                {
                    _console.WithColor(ConsoleColor.Red,
                        c => c.Error.WriteLine("There are no commands defined in this application."));

                    return -1;
                }
                // Fail if specified a command which is not defined
                if (!commandInput.CommandName.IsNullOrWhiteSpace() && matchingCommandSchema == null)
                {
                    _console.WithColor(ConsoleColor.Red,
                        c => c.Error.WriteLine($"Specified command [{commandInput.CommandName}] is not defined."));

                    return -1;
                }

                // Show version if it was requested without specifying a command
                if (IsVersionRequested(commandInput) && commandInput.CommandName.IsNullOrWhiteSpace())
                {
                    _console.Output.WriteLine(_applicationMetadata.VersionText);

                    return 0;
                }

                // Show help if it was requested
                if (IsHelpRequested(commandInput))
                {
                    _commandHelpTextRenderer.RenderHelpText(_applicationMetadata, availableCommandSchemas, matchingCommandSchema);

                    return 0;
                }

                // Show help if command wasn't specified but a default command isn't defined
                if (commandInput.CommandName.IsNullOrWhiteSpace() && matchingCommandSchema == null)
                {
                    _commandHelpTextRenderer.RenderHelpText(_applicationMetadata, availableCommandSchemas);

                    return 0;
                }

                // Create an instance of the command
                var command = _commandFactory.CreateCommand(matchingCommandSchema.Type);

                // Populate command with options according to its schema
                _commandInitializer.InitializeCommand(command, matchingCommandSchema, commandInput);

                await command.ExecuteAsync(_console);

                return 0;
            }
            catch (Exception ex)
            {
                _console.WithColor(ConsoleColor.Red, c => c.Error.WriteLine(ex));

                return ex is CommandErrorException errorException ? errorException.ExitCode : -1;
            }
        }
    }

    public partial class CliApplication
    {
        private static ApplicationMetadata GetDefaultApplicationMetadata()
        {
            // Entry assembly is null in tests
            var entryAssembly = Assembly.GetEntryAssembly();

            var title = entryAssembly?.GetName().Name ?? "App";
            var executableName = Path.GetFileNameWithoutExtension(entryAssembly?.Location) ?? "app";
            var versionText = entryAssembly?.GetName().Version.ToString() ?? "1.0";

            return new ApplicationMetadata(title, executableName, versionText);
        }

        private static IReadOnlyList<Type> GetDefaultCommandTypes()
        {
            // Entry assembly is null in tests
            var entryAssembly = Assembly.GetEntryAssembly();

            if (entryAssembly == null)
                return Type.EmptyTypes;

            return entryAssembly.ExportedTypes.Where(t => t.Implements(typeof(ICommand))).ToArray();
        }
    }
}