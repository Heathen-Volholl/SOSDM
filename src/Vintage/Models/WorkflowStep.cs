using System;

namespace SOSDM.Vintage
{
    public class WorkflowStep
    {
        public string Name { get; set; }
        public string Status { get; set; } // Pending, Running, Completed, Failed
        public string Details { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
    }
}