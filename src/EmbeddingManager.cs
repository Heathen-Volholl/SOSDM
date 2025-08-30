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

        // Keep this in sync with your model (MiniLM is usually 384)
        private const int MaxSeqLen = 128;

        public async Task InitializeAsync()
        {
            var modelPath = "models/all-MiniLM-L6-v2.onnx";

            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Embedding model not found: {modelPath}");

            var options = new SessionOptions
            {
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
            };

            _session = new InferenceSession(modelPath, options);
            await Task.Yield();
        }

        public async Task<float[]> GenerateEmbedding(string text)
        {
            try
            {
                await Task.Yield();

                // --- ultra-simple tokenizer (placeholder) ---
                var tokens = (text ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Take(MaxSeqLen)
                    .ToArray();

                // crude ids by hash; in production, use the model's proper tokenizer
                var tokenIds = tokens.Select(t => Math.Abs(t.GetHashCode()) % 30000).ToArray();

                // pad
                var inputIds = new long[MaxSeqLen];
                var attention = new long[MaxSeqLen];
                var tokenTypes = new long[MaxSeqLen]; // all zeros

                var count = Math.Min(tokenIds.Length, MaxSeqLen);
                for (int i = 0; i < count; i++)
                {
                    inputIds[i] = tokenIds[i];
                    attention[i] = 1;
                    tokenTypes[i] = 0;
                }

                // build tensors [1, seq]
                var idsTensor = new DenseTensor<long>(inputIds, new[] { 1, MaxSeqLen });
                var attnTensor = new DenseTensor<long>(attention, new[] { 1, MaxSeqLen });
                var ttTensor = new DenseTensor<long>(tokenTypes, new[] { 1, MaxSeqLen });

                // Most sentence-transformers BERT-ish ONNX expect these three names:
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", idsTensor),
                    NamedOnnxValue.CreateFromTensor("attention_mask", attnTensor),
                    NamedOnnxValue.CreateFromTensor("token_type_ids", ttTensor)
                };

                using var results = _session.Run(inputs);

                // Try known output names first; otherwise take the first output
                Tensor<float> output;

                if (results.Any(r => r.Name == "last_hidden_state"))
                {
                    output = results.First(r => r.Name == "last_hidden_state").AsTensor<float>();
                    // shape: [1, seq, hidden] -> mean pool using attention_mask
                    return MeanPool(output, attention);
                }
                else if (results.Any(r => r.Name == "sentence_embedding"))
                {
                    output = results.First(r => r.Name == "sentence_embedding").AsTensor<float>();
                    // shape: [1, hidden]
                    return output.ToArray();
                }
                else
                {
                    // fallback: take first output
                    var first = results.First().AsTensor<float>();
                    if (first.Dimensions.Length == 3)
                    {
                        // [1, seq, hidden]
                        return MeanPool(first, attention);
                    }
                    else
                    {
                        // [1, hidden] or [hidden]
                        return first.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Embedding error: {ex.Message}");
                // SAFE FALLBACK: return empty -> ingestion will still store the document
                return Array.Empty<float>();
            }
        }

        private float[] MeanPool(Tensor<float> tokenEmbeds, long[] attentionMask)
        {
            // tokenEmbeds shape: [1, seq, hidden]
            int seq = tokenEmbeds.Dimensions[1];
            int hidden = tokenEmbeds.Dimensions[2];

            var sum = new float[hidden];
            float denom = 0f;

            for (int i = 0; i < seq; i++)
            {
                if (i < attentionMask.Length && attentionMask[i] == 1)
                {
                    for (int h = 0; h < hidden; h++)
                    {
                        sum[h] += tokenEmbeds[0, i, h];
                    }
                    denom += 1f;
                }
            }

            if (denom <= 0f) denom = 1f;
            for (int h = 0; h < hidden; h++)
                sum[h] /= denom;

            return sum;
        }
    }
}
