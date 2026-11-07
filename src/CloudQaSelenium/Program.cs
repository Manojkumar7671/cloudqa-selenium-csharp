using System.Text.Json;
using CloudQaSelenium;

var settings = AppSettings.Load(args);
if (settings.ShowHelp)
{
    Console.WriteLine("CloudQaSelenium - fill First Name, Gender (Male), Country, and State on a form.");
    Console.WriteLine("Usage: dotnet run --project src/CloudQaSelenium -- [--url URL] [--first-name TEXT] [--country TEXT] [--state TEXT] [--timeout SECONDS] [--headless true|false] [--chrome-binary PATH]");
    return 0;
}
if (string.IsNullOrWhiteSpace(settings.Url) || !Uri.TryCreate(settings.Url, UriKind.Absolute, out _))
{
    Console.Error.WriteLine("FAIL Configuration: provide an absolute URL in config.json or with --url.");
    return 2;
}

using var automation = new FormAutomation(settings);
return automation.Execute();

namespace CloudQaSelenium
{
    internal sealed class AppSettings
    {
        public string Url { get; set; } = "http://127.0.0.1:8765/fixture-1.html";
        public string FirstName { get; set; } = "Avery";
        public string Country { get; set; } = "United States";
        public string State { get; set; } = "California";
        public int TimeoutSeconds { get; set; } = 10;
        public bool Headless { get; set; } = true;
        public string ChromeBinary { get; set; } = "";
        public bool ShowHelp { get; set; }

        public static AppSettings Load(string[] args)
        {
            var settings = new AppSettings();
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            if (!File.Exists(configPath))
                configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var configured = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (configured is not null) settings = configured;
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"Invalid config.json: {ex.Message}");
                    Environment.Exit(2);
                }
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg is "--help" or "-h") { settings.ShowHelp = true; continue; }
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Unexpected argument: {arg}. Use --help for supported options.");
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}.");
                var value = args[++i];
                switch (arg.ToLowerInvariant())
                {
                    case "--url": settings.Url = value; break;
                    case "--first-name": settings.FirstName = value; break;
                    case "--country": settings.Country = value; break;
                    case "--state": settings.State = value; break;
                    case "--timeout":
                        if (!int.TryParse(value, out var seconds) || seconds < 1 || seconds > 300)
                            throw new ArgumentException("--timeout must be an integer from 1 to 300.");
                        settings.TimeoutSeconds = seconds;
                        break;
                    case "--headless":
                        if (!bool.TryParse(value, out var headless)) throw new ArgumentException("--headless must be true or false.");
                        settings.Headless = headless;
                        break;
                    case "--chrome-binary": settings.ChromeBinary = value; break;
                    default: throw new ArgumentException($"Unknown option: {arg}. Use --help for supported options.");
                }
            }
            if (settings.TimeoutSeconds < 1) settings.TimeoutSeconds = 10;
            return settings;
        }
    }
}
