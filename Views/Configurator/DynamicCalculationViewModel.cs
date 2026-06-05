using System;

namespace BIS.ERP.Models.Configurator
{
    public class DynamicCalculationViewModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string TargetField { get; set; } = string.Empty;
        public string CalculationType { get; set; } = "Formula";
        public string Formula { get; set; } = string.Empty;
        public bool IsAuto { get; set; }
        public int ExecutionOrder { get; set; }
    }
}