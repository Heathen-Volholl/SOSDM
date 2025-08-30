using System;
using System.Collections.Generic;

namespace SOSDM
{
    // Data Models
    public class Document
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Authors { get; set; }
        public string Abstract { get; set; }
        public string Content { get; set; }
        public string Source { get; set; }
        public string PublicationDate { get; set; }
        public string Keywords { get; set; }
        public float[] Embedding { get; set; }
    }
    
    public class DocumentEmbedding
    {
        public string DocumentId { get; set; }
        public float[] Vector { get; set; }
    }
    
    public class AnalysisResult
    {
        public string Consensus { get; set; }
        public List<string> Contradictions { get; set; } = new List<string>();
        public List<string> KeyFindings { get; set; } = new List<string>();
    }
    
    public class DatabaseStatistics
    {
        public int DocumentCount { get; set; }
        public int EmbeddingCount { get; set; }
    }
}
