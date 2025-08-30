using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SOSDM
{
    // DataHarvester
    public class DataHarvester : IDisposable
    {
        private readonly HttpClient _httpClient;
        
        public DataHarvester()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SOSDM/1.0 Academic Research Tool");
        }
        
        public async Task<List<Document>> HarvestArxiv(string query, int maxResults = 100)
        {
            var documents = new List<Document>();
            
            try
            {
                var url = $"http://export.arxiv.org/api/query?search_query=all:{query}&start=0&max_results={maxResults}";
                var response = await _httpClient.GetStringAsync(url);
                
                // Parse arXiv XML response (very simplified)
                var lines = response.Split('\n');
                
                Document currentDoc = null;
                foreach (var line in lines)
                {
                    if (line.Contains("<entry>"))
                    {
                        currentDoc = new Document { Id = Guid.NewGuid().ToString() };
                    }
                    else if (line.Contains("<title>") && currentDoc != null)
                    {
                        currentDoc.Title = ExtractXmlValue(line, "title");
                    }
                    else if (line.Contains("<summary>") && currentDoc != null)
                    {
                        currentDoc.Abstract = ExtractXmlValue(line, "summary");
                    }
                    else if (line.Contains("</entry>") && currentDoc != null)
                    {
                        currentDoc.Source = "arXiv";
                        documents.Add(currentDoc);
                        currentDoc = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"arXiv harvesting error: {ex.Message}");
            }
            
            return documents;
        }
        
        public async Task<List<Document>> HarvestSemanticScholar(string query, int maxResults = 100)
        {
            var documents = new List<Document>();
            
            try
            {
                var url = $"https://api.semanticscholar.org/graph/v1/paper/search?query={Uri.EscapeDataString(query)}&limit={maxResults}&fields=title,abstract,authors,year";
                var response = await _httpClient.GetStringAsync(url);
                
                var jsonDoc = JsonDocument.Parse(response);
                var papers = jsonDoc.RootElement.GetProperty("data");
                
                foreach (var paper in papers.EnumerateArray())
                {
                    var document = new Document
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = paper.GetProperty("title").GetString(),
                        Abstract = paper.TryGetProperty("abstract", out var abstractProp) ? abstractProp.GetString() : null,
                        Source = "Semantic Scholar"
                    };
                    
                    if (paper.TryGetProperty("authors", out var authorsProp))
                    {
                        var authors = authorsProp.EnumerateArray()
                            .Select(a => a.GetProperty("name").GetString())
                            .ToArray();
                        document.Authors = string.Join(", ", authors);
                    }
                    
                    if (paper.TryGetProperty("year", out var yearProp))
                    {
                        document.PublicationDate = yearProp.GetInt32().ToString();
                    }
                    
                    documents.Add(document);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Semantic Scholar harvesting error: {ex.Message}");
            }
            
            return documents;
        }
        
        private string ExtractXmlValue(string xmlLine, string tagName)
        {
            var startTag = $"<{tagName}>";
            var endTag = $"</{tagName}>";
            
            var startIndex = xmlLine.IndexOf(startTag, StringComparison.Ordinal);
            var endIndex = xmlLine.IndexOf(endTag, StringComparison.Ordinal);
            
            if (startIndex >= 0 && endIndex > startIndex)
            {
                startIndex += startTag.Length;
                return xmlLine.Substring(startIndex, endIndex - startIndex).Trim();
            }
            
            return string.Empty;
        }
        
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
