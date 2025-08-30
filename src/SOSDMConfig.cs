using System;
using System.IO;
using System.Text.Json;

namespace SOSDM
{
    // SOSDMConfig
    public class SOSDMConfig
    {
        public string ModelDirectory { get; set; } = "models";
        public string DatabasePath { get; set; } = "sosdm.db";
        public string ObjectDatabasePath { get; set; } = "sosdm_objects.db";
        public int MaxQueryResults { get; set; } = 20;
        public int EmbeddingDimensions { get; set; } = 384;
        public bool EnableSelfHealing { get; set; } = true;
        public bool EnableREFALProcessing { get; set; } = false;
        public string REFALExecutablePath { get; set; } = "refal5lambda.exe";
        public int MaxConcurrentProcessing { get; set; } = 4;

        // NEW: toggle the ingestion quality gate
        public bool EnableQualityGate { get; set; } = true;

        public static SOSDMConfig LoadFromFile(string configPath = "sosdm_config.json")
        {
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    return JsonSerializer.Deserialize<SOSDMConfig>(json) ?? new SOSDMConfig();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Config loading error: {ex.Message}, using defaults");
                }
            }

            var config = new SOSDMConfig();
            SaveToFile(config, configPath);
            return config;
        }

        public static void SaveToFile(SOSDMConfig config, string configPath = "sosdm_config.json")
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config saving error: {ex.Message}");
            }
        }
    }
}
