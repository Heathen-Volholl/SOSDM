// Vintage/VintageToolsManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SOSDM.Vintage
{
    public class VintageToolsManager
    {
        private readonly SOSDMConfig _config;
        private readonly REFALProcessor _refal;
        private readonly RulesEngine _rules;
        private readonly WorkflowExecutor _workflows;
        private readonly LivingstoneMonitor _livingstone;

        public VintageToolsManager(SOSDMConfig config)
        {
            _config = config;
            _refal = new REFALProcessor(config.REFALExecutablePath);
            _rules = new RulesEngine();
            _workflows = new WorkflowExecutor();
            _livingstone = new LivingstoneMonitor();
        }

        public async Task<string> ProcessTextWithREFAL(string input, string ruleName)
        {
            if (!_config.EnableREFALProcessing) return input;
            try { return await _refal.ProcessText(input, ruleName); }
            catch (Exception ex) { Console.WriteLine($"REFAL processing failed: {ex.Message}"); return input; }
        }

        public bool EvaluateBusinessRule(string ruleName, object document)
        {
            try { return _rules.EvaluateRule(ruleName, ExtractFacts(document)); }
            catch (Exception ex) { Console.WriteLine($"Rule evaluation failed: {ex.Message}"); return true; }
        }

        public async Task<WorkflowResult> ExecuteWorkflow(string workflowName, object context)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(context)) ?? new Dictionary<string, object>();
                return await _workflows.ExecuteWorkflow(workflowName, dict);
            }
            catch (Exception ex)
            {
                return new WorkflowResult { WorkflowName = workflowName, Success = false, ErrorMessage = ex.Message };
            }
        }

        public void ReportSystemHealth(string component, Dictionary<string, object> metrics)
        {
            _livingstone.UpdateMetrics(component, metrics);
        }

        public async Task<DiagnosticResult> GetSystemDiagnosis()
        {
            return await _livingstone.RunDiagnostics();
        }

        private Dictionary<string, object> ExtractFacts(object document)
        {
            var facts = new Dictionary<string, object>();

            if (document is Document doc)
            {
                facts["WordCount"] = doc.Content?.Split(' ').Length ?? 0;
                facts["HasAbstract"] = !string.IsNullOrEmpty(doc.Abstract);
                facts["FileSize"] = doc.Content?.Length ?? 0;
                facts["NonEnglishCharacterRatio"] = CalculateNonEnglishRatio(doc.Content);
            }

            return facts;
        }

        private double CalculateNonEnglishRatio(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var nonEnglishChars = 0;
            foreach (char c in text) if (c > 127) nonEnglishChars++;
            return (double)nonEnglishChars / text.Length;
        }

        private Dictionary<string, object> ConvertToContextDictionary(object context)
        {
            var json = JsonSerializer.Serialize(context);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                   ?? new Dictionary<string, object>();
        }
    }

    public class LivingstoneMonitor
    {
        private readonly Dictionary<string, Dictionary<string, object>> _componentMetrics = new();

        public void UpdateMetrics(string component, Dictionary<string, object> metrics)
        {
            _componentMetrics[component] = metrics;
        }

        public async Task<DiagnosticResult> RunDiagnostics()
        {
            await Task.Yield();
            var result = new DiagnosticResult { Timestamp = DateTime.UtcNow };

            if (IsMemoryExhausted())
            {
                result.Issues.Add("Memory exhaustion detected - consider reducing batch sizes");
                result.Severity = Math.Max(result.Severity, 2);
            }

            if (IsDiskIssue())
            {
                result.Issues.Add("Disk performance issues - check space and I/O load");
                result.Severity = Math.Max(result.Severity, 2);
            }

            if (IsModelDegraded())
            {
                result.Issues.Add("Model performance degraded - consider retraining");
                result.Severity = Math.Max(result.Severity, 1);
            }

            result.OverallHealth = result.Severity == 0 ? "Healthy" :
                                   result.Severity == 1 ? "Warning" : "Critical";

            return result;
        }

        private bool IsMemoryExhausted()
        {
            var totalMemory = GC.GetTotalMemory(false);
            var memoryLimit = 8L * 1024 * 1024 * 1024; // 8GB
            return totalMemory > memoryLimit * 0.9;
        }

        private bool IsDiskIssue()
        {
            try
            {
                var drive = new DriveInfo("C:");
                return drive.AvailableFreeSpace < drive.TotalSize * 0.1;
            }
            catch { return false; }
        }

        private bool IsModelDegraded() => false;
    }

    public class DiagnosticResult
    {
        public DateTime Timestamp { get; set; }
        public string OverallHealth { get; set; } = "Unknown";
        public int Severity { get; set; } = 0; // 0=OK, 1=Warning, 2=Critical
        public List<string> Issues { get; set; } = new List<string>();
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
    }
}
