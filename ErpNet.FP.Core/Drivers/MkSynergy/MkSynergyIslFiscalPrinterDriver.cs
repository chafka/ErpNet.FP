#nullable enable
namespace ErpNet.FP.Core.Drivers.MkSynergy
{
    using System;
    using System.Collections.Generic;
    using ErpNet.FP.Core.Configuration;
    using ErpNet.FP.Core.Drivers.BgDatecs;
    using Serilog;

    /// <summary>
    /// Driver for North Macedonia Synergy PF-550/PF700 fiscal printers.
    /// Reuses BgDatecsCIslFiscalPrinterDriver's device-info parsing (the raw
    /// device info format is the same 6 comma-separated fields), but connects
    /// a MkSynergyIslFiscalPrinter instead, since this device requires a
    /// different Open Fiscal Receipt command shape than Bulgaria's Datecs
    /// devices - see MkSynergyIslFiscalPrinter for details.
    /// AutoDetect must stay false for this driver: Synergy devices don't have
    /// the "DT"/"DA" serial prefix or "DP"/"WP" model prefix that
    /// BgDatecsCIslFiscalPrinterDriver's autodetect validation requires.
    /// </summary>
    public class MkSynergyIslFiscalPrinterDriver : BgDatecsCIslFiscalPrinterDriver
    {
        public override string DriverName => "mk.sy.c.isl";

        public override IFiscalPrinter Connect(
            IChannel channel,
            ServiceOptions serviceOptions,
            bool autoDetect = true,
            IDictionary<string, string>? options = null)
        {
            var fiscalPrinter = new MkSynergyIslFiscalPrinter(channel, serviceOptions, options);
            var rawDeviceInfoCacheKey = $"isl.{channel.Descriptor}.{DriverName}";
            lock (channel)
            {
                var rawDeviceInfo = Cache.Get(rawDeviceInfoCacheKey);
                if (rawDeviceInfo == null || autoDetect)
                {
                    (rawDeviceInfo, _) = fiscalPrinter.GetRawDeviceInfo();
                    Log.Information($"RawDeviceInfo({channel.Descriptor}): {rawDeviceInfo}");
                    Cache.Store(rawDeviceInfoCacheKey, rawDeviceInfo, TimeSpan.FromSeconds(30));
                }
                fiscalPrinter.Info = ParseDeviceInfo(rawDeviceInfo, autoDetect);
                var (TaxIdentificationNumber, _) = fiscalPrinter.GetTaxIdentificationNumber();
                fiscalPrinter.Info.TaxIdentificationNumber = TaxIdentificationNumber;
                fiscalPrinter.Info.SupportedPaymentTypes = fiscalPrinter.GetSupportedPaymentTypes();
                fiscalPrinter.Info.SupportsSubTotalAmountModifiers = true;
                serviceOptions.ReconfigurePrinterConstants(fiscalPrinter.Info);
                return fiscalPrinter;
            }
        }
    }
}
