using System;

namespace ErpNet.FP.Core
{
    /// <summary>
    /// Represents the capabilities of a connected fiscal printer.
    /// </summary>
    public interface IFiscalPrinter
    {
        /// <summary>
        /// Gets information about the connected device.
        /// </summary>
        /// <returns>Device information.</returns>
        DeviceInfo DeviceInfo { get; }

        /// <summary>
        /// Checks whether the device is currently ready to accept commands.
        /// </summary>
        DeviceStatusWithDateTime CheckStatus();

        /// <summary>
        /// Gets the amount of cash available
        /// </summary>
        DeviceStatusWithCashAmount Cash(Credentials credentials);

        /// <summary>
        /// Sets the device date and time
        /// </summary>
        DeviceStatus SetDateTime(CurrentDateTime currentDateTime);

        /// <summary>
        /// Prints the specified receipt.
        /// </summary>
        /// <param name="receipt">The receipt to print.</param>
        (ReceiptInfo, DeviceStatus) PrintReceipt(Receipt receipt);

        /// <summary>
        /// Validates the receipt object
        /// </summary>
        /// <param name="receipt"></param>
        /// <returns></returns>
        DeviceStatus ValidateReceipt(Receipt receipt);

        /// <summary>
        /// Prints the specified reversal receipt.
        /// </summary>
        /// <param name="reversalReceipt">The reversal receipt.</param>
        /// <returns></returns>
        (ReceiptInfo, DeviceStatus) PrintReversalReceipt(ReversalReceipt reversalReceipt);

        /// <summary>
        /// Validates the reversal receipt object
        /// </summary>
        /// <param name="reversalReceipt"></param>
        /// <returns></returns>
        DeviceStatus ValidateReversalReceipt(ReversalReceipt reversalReceipt);

        /// <summary>
        /// Prints a deposit money note.
        /// </summary>
        /// <param name="amount">The deposited amount. Should be greater than 0.</param>
        DeviceStatus PrintMoneyDeposit(TransferAmount transferAmount);

        /// <summary>
        /// Prints a withdraw money note.
        /// </summary>
        /// <param name="amount">The withdrawn amount. Should be greater than 0.</param>
        DeviceStatus PrintMoneyWithdraw(TransferAmount transferAmount);

        /// <summary>
        /// Validates transfer amount object
        /// </summary>
        /// <param name="transferAmount"></param>
        /// <returns></returns>
        DeviceStatus ValidateTransferAmount(TransferAmount transferAmount);

        /// <summary>
        /// Prints a zreport.
        /// </summary>
        DeviceStatus PrintZReport(Credentials credentials);

        /// <summary>
        /// Prints a xreport.
        /// </summary>
        DeviceStatus PrintXReport(Credentials credentials);

        /// <summary>
        /// Prints duplicate of the last fiscal receipt.
        /// </summary>
        DeviceStatus PrintDuplicate(Credentials credentials);

        /// <summary>
        /// Prints a short financial report over a date range.
        /// Default implementation reports "not supported" - only devices
        /// that override it (see MkSynergyIslFiscalPrinter) actually support it.
        /// </summary>
        DeviceStatus PrintPeriodicReport(PeriodicReport periodicReport)
        {
            var status = new DeviceStatus();
            status.AddError("E999", "Periodic report is not supported by this device.");
            return status;
        }

        /// <summary>
        /// Validates the periodic report object
        /// </summary>
        DeviceStatus ValidatePeriodicReport(PeriodicReport periodicReport)
        {
            var status = new DeviceStatus();
            if (periodicReport.StartDate > periodicReport.EndDate)
            {
                status.AddError("E403", "\"startDate\" should not be after \"endDate\"");
            }
            return status;
        }

        /// <summary>
        /// Raw request.
        /// </summary>
        DeviceStatusWithRawResponse RawRequest(RequestFrame requestFrame);

        /// <summary>
        /// Tries to fix the erroneous state of the device to the normal - ready for printing state
        /// </summary>
        DeviceStatusWithDateTime Reset(Credentials credentials);

        void SetDeadLine(DateTime deadLine);
    }
}