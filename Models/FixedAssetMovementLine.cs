using System;
using System.Collections.Generic;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Строка документа «Учет движения ОС» (табличная часть, копия механизма Счет-фактур).
    /// Хранится в таблице doc_fixed_asset_movement_lines.
    /// </summary>
    public class FixedAssetMovementLineRow
    {
        public Guid Id { get; set; }
        public int LineNumber { get; set; }
        public Guid? AssetId { get; set; }
        public string AssetName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string VatTaxCode { get; set; } = string.Empty;
        public string SalesTaxCode { get; set; } = string.Empty;
        public decimal AmountWithoutTax { get; set; }
        public decimal VatRate { get; set; }
        public decimal VatAmount { get; set; }
        public decimal SalesTaxRate { get; set; }
        public decimal SalesTaxAmount { get; set; }
        public decimal LineTotal { get; set; }
        public decimal AmountCurrency { get; set; }
    }

    /// <summary>
    /// Редактируемая строка табличной части (для привязки к DataGrid).
    /// </summary>
    public class EditableFixedAssetMovementLine : FixedAssetMovementLineRow, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public new Guid? AssetId
        {
            get => base.AssetId;
            set { base.AssetId = value; Raise(nameof(AssetId)); }
        }

        public new string AssetName
        {
            get => base.AssetName;
            set { base.AssetName = value; Raise(nameof(AssetName)); }
        }

        public new bool IsActive
        {
            get => base.IsActive;
            set { base.IsActive = value; Raise(nameof(IsActive)); }
        }

        public new string AccountCode
        {
            get => base.AccountCode;
            set { base.AccountCode = value; Raise(nameof(AccountCode)); }
        }

        public new string AccountName
        {
            get => base.AccountName;
            set { base.AccountName = value; Raise(nameof(AccountName)); }
        }

        public new string VatTaxCode
        {
            get => base.VatTaxCode;
            set { base.VatTaxCode = value; Raise(nameof(VatTaxCode)); }
        }

        public new string SalesTaxCode
        {
            get => base.SalesTaxCode;
            set { base.SalesTaxCode = value; Raise(nameof(SalesTaxCode)); }
        }

        public new decimal AmountWithoutTax
        {
            get => base.AmountWithoutTax;
            set { base.AmountWithoutTax = value; Raise(nameof(AmountWithoutTax)); }
        }

        public new decimal VatRate
        {
            get => base.VatRate;
            set { base.VatRate = value; Raise(nameof(VatRate)); }
        }

        public new decimal VatAmount
        {
            get => base.VatAmount;
            set { base.VatAmount = value; Raise(nameof(VatAmount)); }
        }

        public new decimal SalesTaxRate
        {
            get => base.SalesTaxRate;
            set { base.SalesTaxRate = value; Raise(nameof(SalesTaxRate)); }
        }

        public new decimal SalesTaxAmount
        {
            get => base.SalesTaxAmount;
            set { base.SalesTaxAmount = value; Raise(nameof(SalesTaxAmount)); }
        }

        public new decimal LineTotal
        {
            get => base.LineTotal;
            set { base.LineTotal = value; Raise(nameof(LineTotal)); }
        }

        public new decimal AmountCurrency
        {
            get => base.AmountCurrency;
            set { base.AmountCurrency = value; Raise(nameof(AmountCurrency)); }
        }
    }
}