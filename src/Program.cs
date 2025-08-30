using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using LiteDB;
using LLama;
using LLama.Common;
using Microsoft.ML.OnnxRuntime;
using System.Numerics;
using System.Linq;

namespace SOSDM
{
    // Main Application Entry Point
    public class Program
    {
        private static SOSDMCore _core;
        
        public static async Task Main(string[] args)
        {
            Console.WriteLine("SOSDM (Small Open Source Dataset Model) v1.0");
            Console.WriteLine("Initializing system...");
            
            try
            {
                _core = new SOSDMCore();
                await _core.InitializeAsync();
                
                Console.WriteLine("System ready. Type 'help' for commands.");
                await RunInteractiveMode();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Initialization failed: {ex.Message}");
                Environment.Exit(1);
            }
        }
        
        private static async Task RunInteractiveMode()
        {
            while (true)
            {
                Console.Write("SOSDM> ");
                var input = Console.ReadLine();
                
                if (string.IsNullOrEmpty(input)) continue;
                
                switch (input.ToLower())
                {
                    case "quit":
                    case "exit":
                        return;
                    case "help":
                        ShowHelp();
                        break;
                    case "status":
                        await _core.ShowStatus();
                        break;
                    default:
                        if (input.StartsWith("query "))
                        {
                            var query = input.Substring(6);
                            await _core.ProcessQuery(query);
                        }
                        else if (input.StartsWith("ingest "))
                        {
                            var path = input.Substring(7);
                            await _core.IngestDocuments(path);
                        }
                        else
                        {
                            Console.WriteLine("Unknown command. Type 'help' for available commands.");
                        }
                        break;
                }
            }
        }
        
        private static void ShowHelp()
        {
            Console.WriteLine("\nAvailable Commands:");
            Console.WriteLine("  query <text>    - Search and analyze documents");
            Console.WriteLine("  ingest <path>   - Import documents from directory");
            Console.WriteLine("  status          - Show system status");
            Console.WriteLine("  help            - Show this help");
            Console.WriteLine("  quit/exit       - Exit application\n");
        }
    }
}
