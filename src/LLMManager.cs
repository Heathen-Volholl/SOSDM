using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LLama;
using LLama.Common;

namespace SOSDM
{
    // LLMManager
    public class LLMManager
    {
        private LLamaWeights _weights;
        private LLamaContext _context;
        private InteractiveExecutor _executor;
        
        public async Task InitializeAsync()
        {
            var modelPath = "models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf";
            
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            }
            
            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 2048,
                Seed = 1337,
                GpuLayerCount = 0, // CPU only
                UseMemorymap = true,
                UseMemoryLock = true
            };
            
            _weights = LLamaWeights.LoadFromFile(parameters);
            _context = _weights.CreateContext(parameters);
            _executor = new InteractiveExecutor(_context);
        }
        
        public async Task<string> GenerateResponse(string prompt, int maxTokens = 256)
        {
            var fullPrompt = $"<|system|>You are a helpful AI assistant analyzing scholarly articles.</s>\n<|user|>{prompt}</s>\n<|assistant|>";
            
            var responses = new List<string>();
            await foreach (var token in _executor.InferAsync(fullPrompt, new InferenceParams()
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "</s>", "<|user|>", "<|system|>" },
                Temperature = 0.3f,
                TopP = 0.9f
            }))
            {
                responses.Add(token);
            }
            
            return string.Join("", responses).Trim();
        }
    }
}
