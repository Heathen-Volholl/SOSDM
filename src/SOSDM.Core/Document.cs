namespace SOSDM
{
    public sealed class Document
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Authors { get; set; }
        public string Abstract { get; set; }
        public string Content { get; set; }
        public string Source { get; set; }
        public string PublicationDate { get; set; }
        public string Keywords { get; set; }
        public int    CitationCount { get; set; }
        public float[] Embedding { get; set; }
    }

    // LiteDB model
    public sealed class DocumentEmbedding
    {
        public string Id { get; set; }          // use DocumentId as Id
        public string DocumentId { get; set; }
        public float[] Vector { get; set; }
    }

    public sealed class DatabaseStatistics
    {
        public int DocumentCount { get; set; }
        public int EmbeddingCount { get; set; }
    }
}
