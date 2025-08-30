using System;
using System.IO;
using System.Threading.Tasks;

namespace SOSDM
{
    // DocumentProcessor
    public class DocumentProcessor
    {
        public async Task<Document> ProcessDocument(string filePath)
        {
            try
            {
                var document = new Document
                {
                    Id = Guid.NewGuid().ToString(),
                    Source = filePath
                };
                
                var extension = Path.GetExtension(filePath).ToLower();
                
                switch (extension)
                {
                    case ".txt":
                        document.Content = await File.ReadAllTextAsync(filePath);
                        document.Title = Path.GetFileNameWithoutExtension(filePath);
                        break;
                        
                    case ".pdf":
                        // Use PdfPig or similar for PDF extraction
                        document.Content = await ExtractPdfContent(filePath);
                        document.Title = await ExtractPdfTitle(filePath);
                        break;
                        
                    case ".xml":
                        // Handle arXiv XML format
                        document = await ParseArxivXml(filePath);
                        break;
                        
                    default:
                        return null;
                }
                
                // Extract abstract and keywords using simple heuristics
                ExtractMetadata(document);
                
                return document;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {filePath}: {ex.Message}");
                return null;
            }
        }
        
        private async Task<string> ExtractPdfContent(string filePath)
        {
            // Placeholder - implement with PdfPig or similar
            return "PDF content extraction not yet implemented";
        }
        
        private async Task<string> ExtractPdfTitle(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath);
        }
        
        private async Task<Document> ParseArxivXml(string filePath)
        {
            // Placeholder - implement arXiv XML parsing
            return new Document
            {
                Id = Guid.NewGuid().ToString(),
                Title = "XML parsing not yet implemented",
                Content = await File.ReadAllTextAsync(filePath),
                Source = filePath
            };
        }
        
        private void ExtractMetadata(Document document)
        {
            var content = document.Content;
            
            // Simple abstract extraction
            var abstractMarkers = new[] { "abstract", "Abstract", "ABSTRACT" };
            foreach (var marker in abstractMarkers)
            {
                var index = content.IndexOf(marker);
                if (index >= 0)
                {
                    var start = index + marker.Length;
                    var end = content.IndexOf('\n', start + 200);
                    if (end > start)
                    {
                        document.Abstract = content.Substring(start, end - start).Trim();
                        break;
                    }
                }
            }
        }
    }

