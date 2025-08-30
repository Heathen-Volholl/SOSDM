using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SOSDM.Vintage;  // Add this line - Step 3!

namespace SOSDM
{
    // Core SOSDM System
    public class SOSDMCore
    {
        private DatabaseManager _database;
        private LLMManager _llm;
        private EmbeddingManager _embeddings;
        private DocumentProcessor _processor;
        private AnalysisEngine _analysis;
        private SelfHealingManager _selfHealing;
        
        // Add these vintage tool fields - Step 3!
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
            
            // Add vintage tools initialization - Step 3!
            Console.WriteLine("Initializing vintage tools...");
            var config = SOSDMConfig.LoadFromFile();
            _vintageTools = new VintageToolsManager(config);
            _refal = new REFALProcessor(config.REFALExecutablePath);
            _rules = new RulesEngine();
            _workflows = new WorkflowExecutor();
        }
        
        public async Task ProcessQuery(string query)
        {
            try
            {
                Console.WriteLine($"Processing query: {query}");
                
                // Use REFAL to normalize the query - Step 3 integration!
                if (_vintageTools != null)
                {
                    query = await _vintageTools.ProcessTextWithREFAL(query, "QueryNormalization");
                }
                
                // Find relevant documents
                var documents = await _database.SearchDocuments(query);
                Console.WriteLine($"Found {documents.Count} relevant documents");
                
                // Generate analysis
                var analysis = await _analysis.AnalyzeQuery(query, documents);
                
                // Present results
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
                
                // Use workflow executor for structured ingestion - Step 3 integration!
                if (_workflows != null)
                {
                    var context = new Dictionary<string, object> { { "InputPath", path } };
                    var workflowResult = await _workflows.ExecuteWorkflow("DocumentIngestion", context);
                    
                    if (!workflowResult.Success)
                    {
                        Console.WriteLine($"Workflow failed: {workflowResult.ErrorMessage}");
                        return;
                    }
                }
                
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".pdf") || f.EndsWith(".txt") || f.EndsWith(".xml"))
                    .ToList();
                
                Console.WriteLine($"Found {files.Count} files to process");
                
                foreach (var file in files)
                {
                    // Apply business rules for document quality - Step 3 integration!
                    var fileInfo = new FileInfo(file);
                    var facts = new Dictionary<string, object>
                    {
                        { "FileSize", fileInfo.Length },
                        { "FileName", fileInfo.Name }
                    };
                    
                    if (_rules != null && !_rules.EvaluateRule("DocumentQuality", facts))
                    {
                        Console.WriteLine($"Skipping {fileInfo.Name} - failed quality check");
                        continue;
                    }
                    
                    var document = await _processor.ProcessDocument(file);
                    if (document != null)
                    {
                        // Use REFAL to normalize document content - Step 3 integration!
                        if (_vintageTools != null)
                        {
                            document.Content = await _vintageTools.ProcessTextWithREFAL(document.Content, "ContentNormalization");
                            document.Abstract = await _vintageTools.ProcessTextWithREFAL(document.Abstract ?? "", "AbstractExtraction");
                        }
                        
                        var embedding = await _embeddings.GenerateEmbedding(document.Content);
                        document.Embedding = embedding;
                        
                        await _database.StoreDocument(document);
                        Console.WriteLine($"Processed: {Path.GetFileName(file)}");
                    }
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
            
            // Show vintage tools status - Step 3 integration!
            if (_vintageTools != null)
            {
                var diagnosis = await _vintageTools.GetSystemDiagnosis();
                Console.WriteLine($"Vintage tools health: {diagnosis.OverallHealth}");
            }
        }
    }
}
