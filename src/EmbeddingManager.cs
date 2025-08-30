using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SOSDM
{
    // EmbeddingManager
    public class EmbeddingManager
    {
        private InferenceSession _session;
        
        public async Task InitializeAsync()
        {
            var modelPath = "models/all-MiniLM-L6-v2.onnx";
            
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Embedding model not found: {modelPath}");
            }
            
            var options = new SessionOptions
            {
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
            };
            
            _session = new InferenceSession(modelPath, options);
        }
        
        public async Task<float[]> GenerateEmbedding(string text)
        {
            // Simplified tokenization - in production, use proper tokenizer
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(128).ToArray();
            var tokenIds = tokens.Select(t => t.GetHashCode() % 30000).ToArray();
            
            // Pad to fixed length
            var paddedIds = new long[128];
            for (int i = 0; i < Math.Min(tokenIds.Length, 128); i++)
            {
                paddedIds[i] = Math.Abs(tokenIds[i]);
            }
            
            var inputTensor = new DenseTensor<long>(paddedIds, new[] { 1, 128 });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputTensor)
            };
            
            using var results = _session.Run(inputs);
            var embeddings = results.First().AsTensor<float>().ToArray();
            
            return embeddings.Take(384).ToArray(); // MiniLM produces 384-dim embeddings
        }
    }
}
