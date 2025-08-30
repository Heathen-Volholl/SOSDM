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