using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SOSDM
{
    // Analysis Engine
    public class AnalysisEngine
    {
        private readonly DatabaseManager _database;
        private readonly LLMManager _llm;
        private readonly EmbeddingManager _embeddings;
        
        public AnalysisEngine(DatabaseManager database, LLMManager llm, EmbeddingManager embeddings)
        {
            _database = database;
            _llm = llm;
            _embeddings = embeddings;
        }
        
        public async Task<AnalysisResult> AnalyzeQuery(string query, List<Document> documents)
        {
            var result = new AnalysisResult();
            
            // Generate consensus
            var consensusPrompt = $"Based on these {documents.Count} documents, what is the scientific consensus on: {query}?\n\n" +
                                string.Join("\n\n", documents.Take(3).Select(d => $"Title: {d.Title}\nAbstract: {d.Abstract}"));
            
            result.Consensus = await _llm.GenerateResponse(consensusPrompt);
            
            // Find contradictions
            result.Contradictions = await FindContradictions(documents);
            
            // Extract key findings
            result.KeyFindings = await ExtractKeyFindings(documents);
            
            return result;
        }
        
        private async Task<List<string>> FindContradictions(List<Document> documents)
        {
            // Simplified contradiction detection
            var contradictions = new List<string>();
            
            for (int i = 0; i < Math.Min(documents.Count, 5); i++)
            {
                for (int j = i + 1; j < Math.Min(documents.Count, 5); j++)
                {
                    var prompt = $"Do these two abstracts contradict each other? Answer yes/no and explain briefly.\n\n" +
                               $"Abstract 1: {documents[i].Abstract}\n\n" +
                               $"Abstract 2: {documents[j].Abstract}";
                    
                    var response = await _llm.GenerateResponse(prompt);
                    
                    if (response.ToLower().StartsWith("yes"))
                    {
                        contradictions.Add($"{documents[i].Title} vs {documents[j].Title}: {response}");
                    }
                }
            }
            
            return contradictions;
        }
        
        private async Task<List<string>> ExtractKeyFindings(List<Document> documents)
        {
            var findings = new List<string>();
            
            foreach (var doc in documents.Take(5))
            {
                var prompt = $"What is the key finding or main conclusion from this abstract? Be concise.\n\nAbstract: {doc.Abstract}";
                var finding = await _llm.GenerateResponse(prompt);
                findings.Add($"{doc.Title}: {finding}");
            }
            
            return findings;
        }
    }
}
