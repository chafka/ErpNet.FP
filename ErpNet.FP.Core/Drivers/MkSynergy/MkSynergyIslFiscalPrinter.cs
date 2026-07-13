namespace ErpNet.FP.Core.Drivers.MkSynergy
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;
    using ErpNet.FP.Core.Configuration;
    using ErpNet.FP.Core.Drivers.BgDatecs;

    /// <summary>
    /// Fiscal printer for North Macedonia (UJP) devices using the Synergy
    /// PF-550/PF700 ISL variant - a close relative of Datecs Bulgaria's ISL
    /// protocol, but not identical. Differences from BgDatecsCIslFiscalPrinter,
    /// per the Synergy PF-550 manual (command 30h / 48 "Opening a fiscal
    /// client's receipt"):
    /// - The third field of the Open Fiscal Receipt command is the point of
    ///   sale/till number (TillNmb), not a unique sale number. North Macedonia
    ///   does not use ErpNet.FP's uniqueSaleNumber concept at all.
    /// - Returns (negative quantity) can't go through command 31h
    ///   ("Registration of Sales") - its Sign field is documented as '+' only
    ///   and the device refuses the command outright if a tax group's running
    ///   total would go negative. Command 34h ("Registration and Display")
    ///   documents the same data shape but with a mandatory '+'/'-' Sign, so
    ///   negative-quantity items are re-routed there instead.
    /// </summary>
    public partial class MkSynergyIslFiscalPrinter : BgDatecsCIslFiscalPrinter
    {
        private const byte CommandFiscalReceiptSaleWithSign = 0x34;
        private const byte CommandPeriodicReport = 0x4f;

        private static readonly Regex UniqueSalesNumberPattern =
            new Regex("^[A-Z]{2}[0-9]{6}-[A-Z0-9]{4}-[0-9]{7}$", RegexOptions.Compiled);

        public MkSynergyIslFiscalPrinter(
            IChannel channel,
            ServiceOptions serviceOptions,
            IDictionary<string, string>? options = null)
        : base(channel, serviceOptions, options) { }

        public override IDictionary<string, string>? GetDefaultOptions()
        {
            return new Dictionary<string, string>
            {
                ["Operator.ID"] = "1",
                // Per the Synergy PF-550 manual, the operator password must be
                // 4-6 digits; "000000" is the factory default on a fresh device.
                ["Operator.Password"] = "000000",
                ["Till.Number"] = "1",

                ["Administrator.ID"] = "20",
                ["Administrator.Password"] = "9999"
            };
        }

        public override (string, DeviceStatus) OpenReceipt(
            string uniqueSaleNumber,
            string operatorId,
            string operatorPassword)
        {
            var header = string.Join(",",
                new string[] {
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.ID", "1")
                        :
                        operatorId,
                    String.IsNullOrEmpty(operatorId) ?
                        Options.ValueOrDefault("Operator.Password", "000000").WithMaxLength(Info.OperatorPasswordMaxLength)
                        :
                        operatorPassword,
                    Options.ValueOrDefault("Till.Number", "1")
                });
            return Request(CommandOpenFiscalReceipt, header);
        }

        public override (string, DeviceStatus) AddItem(
            int department,
            string itemText,
            decimal unitPrice,
            TaxGroup taxGroup,
            decimal quantity = 0,
            decimal priceModifierValue = 0,
            PriceModifierType priceModifierType = PriceModifierType.None,
            int itemCode = 999)
        {
            if (quantity >= 0)
            {
                return base.AddItem(department, itemText, unitPrice, taxGroup, quantity, priceModifierValue, priceModifierType, itemCode);
            }

            // Return: ERPNext/POSAwesome model it as a negative quantity with
            // a positive unit price. This device instead expects a positive
            // quantity with a signed price (same total either way), sent via
            // 34h since 31h's Sign field doesn't support '-'.
            var effectiveQuantity = Math.Abs(quantity);
            var effectiveUnitPrice = -Math.Abs(unitPrice);

            var itemData = new StringBuilder();
            itemData
                .Append(itemText.WithMaxLength(Info.ItemTextMaxLength))
                .Append('\t').Append(GetTaxGroupText(taxGroup))
                .Append(effectiveUnitPrice.ToString("+0.00;-0.00", CultureInfo.InvariantCulture));
            if (effectiveQuantity != 0)
            {
                itemData
                    .Append('*')
                    .Append(effectiveQuantity.ToString(CultureInfo.InvariantCulture));
            }
            if (priceModifierType != PriceModifierType.None)
            {
                itemData
                    .Append(
                        priceModifierType == PriceModifierType.DiscountPercent
                        ||
                        priceModifierType == PriceModifierType.SurchargePercent
                        ? ',' : '$')
                    .Append((
                        priceModifierType == PriceModifierType.DiscountPercent
                        ||
                        priceModifierType == PriceModifierType.DiscountAmount
                        ? -priceModifierValue : priceModifierValue).ToString("F2", CultureInfo.InvariantCulture));
            }
            return Request(CommandFiscalReceiptSaleWithSign, itemData.ToString());
        }

        public override DeviceStatus ValidateReceipt(Receipt receipt)
        {
            // Copy of BgFiscalPrinter.ValidateReceipt with two changes for
            // this device's return handling (see AddItem above):
            // - a Sale item's quantity is allowed to be negative
            // - a payment's amount is allowed to be negative, so its total
            //   can match a return receipt's negative items total
            var status = new DeviceStatus();
            if (receipt.Items == null || receipt.Items.Count == 0)
            {
                status.AddError("E410", "Receipt is empty, no items");
                return status;
            }
            if (!String.IsNullOrEmpty(receipt.UniqueSaleNumber))
            {
                var isMatch = UniqueSalesNumberPattern.IsMatch(receipt.UniqueSaleNumber);
                if (!isMatch)
                {
                    status.AddError("E405", "Invalid format of UniqueSaleNumber");
                    return status;
                }
            }
            var itemsTotalAmount = 0.00m;
            var row = 0;
            foreach (var item in receipt.Items)
            {
                row++;

                switch (item.Type)
                {
                    case ItemType.Sale:
                        if (String.IsNullOrEmpty(item.Text))
                        {
                            status.AddError("E407", $"Item {row}: \"text\" is empty");
                        }
                        if (item.PriceModifierValue <= 0 && item.PriceModifierType != PriceModifierType.None)
                        {
                            status.AddError("E403", $"Item {row}: \"priceModifierValue\" should be positive number");
                        }
                        if (item.PriceModifierValue != 0 && item.PriceModifierType == PriceModifierType.None)
                        {
                            status.AddError("E403", $"Item {row}: \"priceModifierValue\" should'nt be \"none\" or empty. You can avoid setting priceModifier if you do not want price modification");
                        }
                        if (item.Department < 0)
                        {
                            status.AddError("E403", $"Item {row}; \"department\" should be positive number or zero.");
                        }
                        if (item.TaxGroup == TaxGroup.Unspecified)
                        {
                            status.AddError("E403", $"Item {row}: \"taxGroup\" shouldn't be \"unspecified\" or empty");
                        }
                        try
                        {
                            GetTaxGroupText(item.TaxGroup);
                        }
                        catch (StandardizedStatusMessageException e)
                        {
                            status.AddError(e.Code, e.Message);
                        }
                        var quantity = Math.Round(item.Quantity == 0m ? 1m : item.Quantity, 3, MidpointRounding.AwayFromZero);
                        var unitPrice = Math.Round(item.UnitPrice, 2, MidpointRounding.AwayFromZero);
                        var itemPrice = Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
                        var itemPriceModifierValue = Math.Round(item.PriceModifierValue, 2, MidpointRounding.AwayFromZero);
                        switch (item.PriceModifierType)
                        {
                            case PriceModifierType.DiscountAmount:
                                itemPrice -= itemPriceModifierValue;
                                break;
                            case PriceModifierType.DiscountPercent:
                                itemPrice -= Math.Round(itemPrice * (itemPriceModifierValue / 100.0m), 2, MidpointRounding.AwayFromZero);
                                break;
                            case PriceModifierType.SurchargeAmount:
                                itemPrice += itemPriceModifierValue;
                                break;
                            case PriceModifierType.SurchargePercent:
                                itemPrice += Math.Round(itemPrice * (itemPriceModifierValue / 100.0m), 2, MidpointRounding.AwayFromZero);
                                break;
                        }
                        itemsTotalAmount += Math.Round(itemPrice, 2, MidpointRounding.AwayFromZero);
                        break;


                    case ItemType.Comment:
                        if (String.IsNullOrEmpty(item.Text))
                        {
                            status.AddError("E407", $"Item {row}: \"text\" is empty");
                        }
                        break;


                    case ItemType.FooterComment:
                        if (String.IsNullOrEmpty(item.Text))
                        {
                            status.AddError("E407", $"Item {row}: \"text\" is empty");
                        }
                        break;


                    case ItemType.SurchargeAmount:
                        if (item.Amount <= 0)
                        {
                            status.AddError("E403", $"Item {row}: \"amount\" should be positive number");
                        }
                        itemsTotalAmount += Math.Round(item.Amount, 2, MidpointRounding.AwayFromZero);
                        break;


                    case ItemType.DiscountAmount:
                        if (item.Amount <= 0)
                        {
                            status.AddError("E403", $"Item {row}: \"amount\" should be positive number");
                        }
                        itemsTotalAmount -= Math.Round(item.Amount, 2, MidpointRounding.AwayFromZero);
                        break;


                    default:
                        break;
                }

                if (!status.Ok)
                {
                    return status;
                }
            }
            if (receipt.Payments?.Count > 0)
            {
                var paymentAmount = 0.00m;
                row = 0;
                foreach (var payment in receipt.Payments)
                {
                    row++;

                    if (payment.Amount >= 0 && payment.PaymentType == PaymentType.Change)
                    {
                        status.AddError("E403", $"Change {row}: \"amount\" should be negative number");
                    }

                    try
                    {
                        if (payment.PaymentType != PaymentType.Change)
                        {
                            // Check if the payment type is supported
                            GetPaymentTypeText(payment.PaymentType);
                        }
                    }
                    catch (StandardizedStatusMessageException e)
                    {
                        status.AddError(e.Code, e.Message);
                    }

                    if (!status.Ok)
                    {
                        status.AddInfo($"Error occured at Payment {row}");
                        return status;
                    }

                    var amount = Math.Round(payment.Amount, 2, MidpointRounding.AwayFromZero);
                    paymentAmount += amount;
                }
                var difference = Math.Abs(paymentAmount - itemsTotalAmount);
                if (difference >= 0.01m && itemsTotalAmount != 0 && difference / Math.Abs(itemsTotalAmount) > 0.00001m)
                {
                    status.AddError("E403", $"Payment total amount ({paymentAmount.ToString(CultureInfo.InvariantCulture)}) should be the same as the items total amount ({itemsTotalAmount.ToString(CultureInfo.InvariantCulture)})");
                }
            }
            return status;
        }

        public DeviceStatus ValidatePeriodicReport(PeriodicReport periodicReport)
        {
            var status = new DeviceStatus();
            if (periodicReport.StartDate == DateTime.MinValue || periodicReport.EndDate == DateTime.MinValue)
            {
                status.AddError("E403", "Both \"startDate\" and \"endDate\" are required");
                return status;
            }
            if (periodicReport.StartDate > periodicReport.EndDate)
            {
                status.AddError("E403", "\"startDate\" should not be after \"endDate\"");
            }
            return status;
        }

        // 4Fh "Sums accumulated in the fiscal memory for a selected period" -
        // prints a short financial report for the given date range (DDMMYY).
        public DeviceStatus PrintPeriodicReport(PeriodicReport periodicReport)
        {
            var header = string.Join(",",
                new string[] {
                    periodicReport.StartDate.ToString("ddMMyy", CultureInfo.InvariantCulture),
                    periodicReport.EndDate.ToString("ddMMyy", CultureInfo.InvariantCulture)
                });
            var (_, status) = Request(CommandPeriodicReport, header);
            return status;
        }
    }
}
