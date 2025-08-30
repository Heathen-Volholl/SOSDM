using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SOSDM.Vintage;

namespace SOSDM
{
    // Core SOSDM System
    public class SOSDMCore
    {
        private SOSDMConfig _config;                 // store config
        private DatabaseManager _database;
        private LLMManager _llm;
        private EmbeddingManager _embeddings;
        private DocumentProcessor _processor;
        private AnalysisEngine _analysis;
        private SelfHealingManager _selfHealing;

        // Vintage tools
        private REFALProcessor _refal;
        private RulesEngine _rules;
        private WorkflowExecutor _workflows;
        private VintageToolsManager _vintageTools;

        public async Task InitializeAsync()
        {
            Console.WriteLine("Initializing database...");
            _database = new DatabaseManager();
            await _database.InitializeAsync();

            Console.WriteLine("Loading language model...");
            _llm = new LLMManager();
            await _llm.InitializeAsync();

            Console.WriteLine("Loading embedding model...");
            _embeddings = new EmbeddingManager();
            await _embeddings.InitializeAsync();

            Console.WriteLine("Setting up document processor...");
            _processor = new DocumentProcessor();

            Console.WriteLine("Initializing analysis engine...");
            _analysis = new AnalysisEngine(_database, _llm, _embeddings);

            Console.WriteLine("Starting self-healing system...");
            _selfHealing = new SelfHealingManager();
            _selfHealing.Start();

            Console.WriteLine("Initializing vintage tools...");
            _config = SOSDMConfig.LoadFromFile();               // keep the config
            _vintageTools = new VintageToolsManager(_config);
            _refal = new REFALProcessor(_config.REFALExecutablePath);
            _rules = new RulesEngine();
            _workflows = new WorkflowExecutor();
        }

        public async Task ProcessQuery(string query)
        {
            try
            {
                Console.WriteLine($"Processing query: {query}");

                if (_vintageTools != null)
                    query = await _vintageTools.ProcessTextWithREFAL(query, "QueryNormalization");

                var documents = await _database.SearchDocuments(query);
                Console.WriteLine($"Found {documents.Count} relevant documents");

                var analysis = await _analysis.AnalyzeQuery(query, documents);

                Console.WriteLine("\nAnalysis Results:");
                Console.WriteLine($"Consensus: {analysis.Consensus}");
                Console.WriteLine($"Contradictions: {string.Join("; ", analysis.Contradictions)}");
                Console.WriteLine($"Key Findings: {string.Join("; ", analysis.KeyFindings)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Query processing failed: {ex.Message}");
                _selfHealing.ReportIssue("QueryProcessing", ex);
            }
        }

        public async Task IngestDocuments(string path)
        {
            try
            {
                Console.WriteLine($"Ingesting documents from: {path}");

                if (_workflows != null)
                {
                    var context = new Dictionary<string, object> { { "InputPath", path } };
                    var workflowResult = await _workflows.ExecuteWorkflow("DocumentIngestion", context);
                    if (!workflowResult.Success)
                    {
                        Console.WriteLine($"Workflow failed: {workflowResult.ErrorMessage}");
                        // not fatal; continue to raw ingestion
                    }
                }

                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"Found {files.Count} files to process");

                foreach (var file in files)
                {
                    var fi = new FileInfo(file);

                    // Quality gate (can be disabled in config)
                    if (_config?.EnableQualityGate == true && _rules != null)
                    {
                        var facts = new Dictionary<string, object>
                        {
                            { "FileSize", fi.Length },
                            { "FileName", fi.Name },
                            { "Extension", fi.Extension.ToLowerInvariant() }
                        };

                        if (!_rules.EvaluateRule("DocumentQuality", facts))
                        {
                            // FIX: use fi.Name (fileInfo doesn't exist)
                            Console.WriteLine($"Skipping {fi.Name} - failed quality check ({_rules.LastReason})");
                            continue;
                        }
                    }

                    var document = await _processor.ProcessDocument(file);
                    if (document == null)
                    {
                        Console.WriteLine($"Skipping {fi.Name} - processor returned null");
                        continue;
                    }

                    if (_vintageTools != null)
                    {
                        document.Content = await _vintageTools.ProcessTextWithREFAL(document.Content ?? "", "ContentNormalization");
                        document.Abstract = await _vintageTools.ProcessTextWithREFAL(document.Abstract ?? "", "AbstractExtraction");
                    }

var embedding = await _embeddings.GenerateEmbedding(document.Content ?? "");
if (embedding != null && embedding.Length > 0)
{
    document.Embedding = embedding;
}
else
{
    Console.WriteLine($"Warning: embedding unavailable for {fi.Name}; storing document without embedding.");
}

await _database.StoreDocument(document);
Console.WriteLine($"Processed: {fi.Name}");

                }

                Console.WriteLine("Ingestion complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ingestion failed: {ex.Message}");
                _selfHealing.ReportIssue("DocumentIngestion", ex);
            }
        }

        public async Task ShowStatus()
        {
            var stats = await _database.GetStatistics();
            Console.WriteLine("\nSystem Status:");
            Console.WriteLine($"Documents in database: {stats.DocumentCount}");
            Console.WriteLine($"Total embeddings: {stats.EmbeddingCount}");
            Console.WriteLine($"Memory usage: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            Console.WriteLine($"Self-healing status: {_selfHealing.GetStatus()}");

            if (_vintageTools != null)
            {
                var diagnosis = await _vintageTools.GetSystemDiagnosis();
                Console.WriteLine($"Vintage tools health: {diagnosis.OverallHealth}");
            }
        }
    }
}
