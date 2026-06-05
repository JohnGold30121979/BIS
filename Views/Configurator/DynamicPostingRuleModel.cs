using System;

namespace BIS.ERP.Models.Configurator
{
    public class DynamicPostingRuleModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string DebitAccountExpression { get; set; } = string.Empty;
        public string CreditAccountExpression { get; set; } = string.Empty;
        public string AmountExpression { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public int Order { get; set; }
    }
}