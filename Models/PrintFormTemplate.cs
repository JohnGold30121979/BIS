using System.Collections.Generic;

namespace BIS.ERP.Models
{
    public class PrintFormTemplate
    {
        public string SourceFormat { get; set; } = "FoxProFRX";
        public string OriginalFileName { get; set; } = string.Empty;
        public double PageWidth { get; set; } = 210;
        public double PageHeight { get; set; } = 297;
        public List<PrintFormBand> Bands { get; set; } = new();
        public List<PrintFormElement> Elements { get; set; } = new();
    }

    public class PrintFormBand
    {
        public string Type { get; set; } = "Detail";
        public double Top { get; set; }
        public double Height { get; set; }
        public int Order { get; set; }
    }

    public class PrintFormElement
    {
        public string Type { get; set; } = "Text";
        public string Text { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public string BandType { get; set; } = "Detail";
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string FontName { get; set; } = "Arial";
        public double FontSize { get; set; } = 9;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string Alignment { get; set; } = "Left";
        public string BorderStyle { get; set; } = "None";
        public int Order { get; set; }
    }
}
