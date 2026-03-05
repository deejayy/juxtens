using System.IO;
using System.Text.Json;
using Juxtens.Logger;

namespace Juxtens.Client;

public sealed class ClientConfig
{
    public string LastConnectionAddress { get; set; } = "127.0.0.1:5021";
    public bool AutoConnectOnStartup { get; set; } = false;

    private static readonly string ConfigFilePath = Path.Combine("juxtens.json");

    public static ClientConfig Load(ILogger logger)
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<ClientConfig>(json);
                if (config != null)
                {
                    logger.Info($"Config loaded from {ConfigFilePath}");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("Failed to load config, using defaults", ex);
        }

        logger.Info("Using default config");
        return new ClientConfig();
    }

    public void Save(ILogger logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
            logger.Info($"Config saved to {ConfigFilePath}");
        }
        catch (Exception ex)
        {
            logger.Error("Failed to save config", ex);
        }
    }
}
