using System;
using System.Collections.Generic;

namespace BIS.ERP.Models
{
    public class ReferenceItem
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public HashSet<string> LookupKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
