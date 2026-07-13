namespace ErpNet.FP.Core
{
    using System;
    using Newtonsoft.Json;

    /// <summary>
    /// A short financial report over a date range (not all devices support this).
    /// </summary>
    public class PeriodicReport
    {
        [JsonProperty(Required = Required.Always)]
        public DateTime StartDate = DateTime.MinValue;

        [JsonProperty(Required = Required.Always)]
        public DateTime EndDate = DateTime.MinValue;
    }
}
