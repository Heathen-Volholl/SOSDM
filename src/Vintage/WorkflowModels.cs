using System.Collections.Generic;

namespace SOSDM.Vintage
{
    public class WorkflowResult
    {
        public string WorkflowName { get; set; } = "";
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    }

    public class WorkflowStep
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";   // "Completed", "Skipped", "Failed"
        public string Details { get; set; } = "";
    }
}
