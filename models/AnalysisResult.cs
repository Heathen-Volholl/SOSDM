    public class AnalysisResult
    {
        public string Consensus { get; set; }
        public List<string> Contradictions { get; set; } = new List<string>();
        public List<string> KeyFindings { get; set; } = new List<string>();
    }