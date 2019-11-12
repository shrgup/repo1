// ----------------------------------------------------------------------------
// <copyright file="OIPublishingService.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------
namespace Microsoft.VisualStudio.Services.OssEngineering.Telemetry
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using Microsoft.TeamFoundation.Framework.Server;
    using Microsoft.VisualStudio.Services.OssEngineering.LoggerPort;
    using Microsoft.VisualStudio.Services.OssEngineering.Model;

    public class OIPublishingService : IOIPublishingService
    {
        private static Dictionary<OssKpiEventName, OssKpiDefinition> kpiDictionary;
        private static Dictionary<OssCiEventName, OssCIDefinition> ciDictionary;
        private const string EventNameKey = "EventName";
        private const string ComponentIdKey = "ComponentId";
        private const string RequestIdKey = "RequestId";

        private IOIPublisher oiPublisher;

        static OIPublishingService()
        {
            // Kpi definitions
            kpiDictionary = new Dictionary<OssKpiEventName, OssKpiDefinition>();

            kpiDictionary.Add(
                OssKpiEventName.NewRequest,
                new OssKpiDefinition { KpiName = KpiConstants.KpiNames.NewRequest, EventId = KpiConstants.EventIds.NewOssRequest, HigherIsBetter = false, Area = KpiConstants.KpiAreas.Load, DisplayName = "New OSS Requests", Description = "Total number of new OSS requests" });

            kpiDictionary.Add(
                OssKpiEventName.FailedRequest,
                new OssKpiDefinition { KpiName = KpiConstants.KpiNames.FailedRequest, EventId = KpiConstants.EventIds.FailedOssRequest, HigherIsBetter = false, Area = KpiConstants.KpiAreas.Infra, DisplayName = "Failed OSS Requests", Description = "Total number of failed OSS requests" });

            kpiDictionary.Add(
                OssKpiEventName.SouceImpounderLatencyInSeconds,
                new OssKpiDefinition { KpiName = KpiConstants.KpiNames.SouceImpounderLatencyInSeconds, EventId = KpiConstants.EventIds.SourceImpounder, HigherIsBetter = false, Area = KpiConstants.KpiAreas.Performance, DisplayName = "Source Impounder Latency", Description = "The amount of time the Souce Impounder takes to impound sources in an internal repository" });

            kpiDictionary.Add(
                OssKpiEventName.SyncLatencyInSeconds,
                new OssKpiDefinition { KpiName = KpiConstants.KpiNames.SyncLatencyInSeconds, EventId = KpiConstants.EventIds.SyncLatencyInSeconds, HigherIsBetter = false, Area = KpiConstants.KpiAreas.Performance, DisplayName = "Sync Latency", Description = "The amount of time the Sync Job takes to sync sources in an internal repository" });

            kpiDictionary.Add(
                OssKpiEventName.SyncIsNeeded,
                new OssKpiDefinition { KpiName = KpiConstants.KpiNames.SyncIsNeeded, EventId = KpiConstants.EventIds.SyncIsNeeded, HigherIsBetter = false, Area = KpiConstants.KpiAreas.Infra, DisplayName = "Necessary Sync", Description = "Total number of sync jobs that were needed." });

            kpiDictionary.Add(
                OssKpiEventName.SyncFailed,
                new OssKpiDefinition { KpiName = KpiConstants.KpiNames.SyncFailed, EventId = KpiConstants.EventIds.SyncFailed, HigherIsBetter = false, Area = KpiConstants.KpiAreas.Infra, DisplayName = "Sync Jobs Failed", Description = "Total number of sync jobs failed" });

            kpiDictionary.Add(
                OssKpiEventName.RequestProcessingTimeInSeconds,
                new OssKpiDefinition { KpiName = KpiConstants.KpiNames.RequestProcessingTimeInSeconds, EventId = KpiConstants.EventIds.RequestProcessingTime, HigherIsBetter = false, Area = KpiConstants.KpiAreas.Performance, DisplayName = "Request Processing Time", Description = "The amount of time the request takes to complete from end to end" });


            // Ci definitions
            ciDictionary = new Dictionary<OssCiEventName, OssCIDefinition>();
            ciDictionary.Add(OssCiEventName.RepoSize, new OssCIDefinition { Name = CiConstants.CiNames.RepoSize, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.ExternalOssSource, new OssCIDefinition { Name = CiConstants.CiNames.ExternalOssSource, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.ExternalProjectUrl, new OssCIDefinition { Name = CiConstants.CiNames.ExternalProjectUrl, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.AccountId, new OssCIDefinition { Name = CiConstants.CiNames.AccountId, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.ComponentName, new OssCIDefinition { Name = CiConstants.CiNames.ComponentName, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.SyncPartiallySucceeded, new OssCIDefinition { Name = CiConstants.CiNames.SyncPartiallySucceeded, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.SyncLatencyInSeconds, new OssCIDefinition { Name = CiConstants.CiNames.SyncLatencyInSeconds, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.SyncIsNeeded, new OssCIDefinition { Name = CiConstants.CiNames.SyncIsNeeded, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.SyncFailed, new OssCIDefinition { Name = CiConstants.CiNames.SyncFailed, Area = CiConstants.CiAreas.Component });
            ciDictionary.Add(OssCiEventName.CreatedBy, new OssCIDefinition { Name = CiConstants.CiNames.CreatedBy, Area = CiConstants.CiAreas.Request });
        }

        public OIPublishingService()
        {
        }

        public OIPublishingService(IOIPublisher publisher)
        {
            this.oiPublisher = publisher;
        }

        public void PublishKpi(OssKpiEventName eventName, double value)
        {
            Logger.WriteStartMessage(OssTracePoints.TelemetryTracePoints.PublishKpiStart, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name + string.Format(CultureInfo.InvariantCulture, "({0}, {1})", eventName.ToString(), value));
            var ossKpiDefinition = kpiDictionary[eventName];
            this.DefineKpiIfRequired(ossKpiDefinition);

            this.oiPublisher.PublishOSSKpi(ossKpiDefinition.KpiName, value, ossKpiDefinition.Area);
            Logger.WriteEndMessage(OssTracePoints.TelemetryTracePoints.PublishKpiEnd, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name);
        }

        public void PublishComponentSpecificCi(OssCiEventName eventName, int componentId, string value)
        {
            Logger.WriteStartMessage(OssTracePoints.TelemetryTracePoints.PublishCiStart, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name + string.Format(CultureInfo.InvariantCulture, "({0}, {1})", eventName.ToString(), value));
            var ciData = new CustomerIntelligenceData();
            ciData.Add(EventNameKey, ciDictionary[eventName].Name);
            ciData.Add(ComponentIdKey, componentId);
            ciData.Add(ciDictionary[eventName].Name, value);

            this.oiPublisher.PublishOSSCi(ciData, ciDictionary[eventName].Area);
            Logger.WriteEndMessage(OssTracePoints.TelemetryTracePoints.PublishCiEnd, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name);
        }

        public void PublishComponentSpecificCi(OssCiEventName eventName, int componentId, double value)
        {
            Logger.WriteStartMessage(OssTracePoints.TelemetryTracePoints.PublishCiStart, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name + string.Format(CultureInfo.InvariantCulture, "({0}, {1})", eventName.ToString(), value));
            var ciData = new CustomerIntelligenceData();
            ciData.Add(EventNameKey, ciDictionary[eventName].Name);
            ciData.Add(ComponentIdKey, componentId);
            ciData.Add(ciDictionary[eventName].Name, value);

            this.oiPublisher.PublishOSSCi(ciData, ciDictionary[eventName].Area);
            Logger.WriteEndMessage(OssTracePoints.TelemetryTracePoints.PublishCiEnd, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name);
        }

        public void PublishRequestSpecificCi(OssCiEventName eventName, int requestId, string value)
        {
            Logger.WriteStartMessage(OssTracePoints.TelemetryTracePoints.PublishCiStart, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name + string.Format(CultureInfo.InvariantCulture, "({0}, {1})", eventName.ToString(), value));
            var ciData = new CustomerIntelligenceData();
            ciData.Add(EventNameKey, ciDictionary[eventName].Name);
            ciData.Add(RequestIdKey, requestId);
            ciData.Add(ciDictionary[eventName].Name, value);

            this.oiPublisher.PublishOSSCi(ciData, ciDictionary[eventName].Area);
            Logger.WriteEndMessage(OssTracePoints.TelemetryTracePoints.PublishCiEnd, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name);
        }


        public void DefineKpiIfRequired(OssKpiDefinition kpiDefinition)
        {
            Logger.WriteStartMessage(OssTracePoints.TelemetryTracePoints.DefineKpiIfRequiredStart, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name);
            var isKpiDefined = this.oiPublisher.IsKpiDefined(kpiDefinition.Area, kpiDefinition.KpiName, KpiConstants.Scope);
            if (!isKpiDefined)
            {
                var definition = new KpiDefinition()
                {
                    Name = kpiDefinition.KpiName,
                    Scope = KpiConstants.Scope,
                    DisplayName = kpiDefinition.DisplayName,
                    Description = kpiDefinition.Description,
                    Area = kpiDefinition.Area,
                    HigherIsBetter = kpiDefinition.HigherIsBetter
                };

                if (kpiDefinition.GoodValue.HasValue)
                {
                    definition.States.Add(new KpiStateDefinition()
                    {
                        KpiState = KpiState.Good,
                        Limit = kpiDefinition.GoodValue.Value,
                        EventId = kpiDefinition.EventId,
                    });
                }

                if (kpiDefinition.WarningValue.HasValue)
                {
                    definition.States.Add(new KpiStateDefinition()
                    {
                        KpiState = KpiState.Warning,
                        Limit = kpiDefinition.WarningValue.Value,
                        EventId = kpiDefinition.EventId,
                    });
                }

                if (kpiDefinition.ErrorValue.HasValue)
                {
                    definition.States.Add(new KpiStateDefinition()
                    {
                        KpiState = KpiState.Error,
                        Limit = kpiDefinition.ErrorValue.Value,
                        EventId = kpiDefinition.EventId,
                    });
                }

                // Critical condition, enable alert.
                if (kpiDefinition.CriticalValue.HasValue)
                {
                    definition.States.Add(new KpiStateDefinition()
                    {
                        KpiState = KpiState.Critical,
                        Limit = kpiDefinition.CriticalValue.Value,
                        EventId = kpiDefinition.EventId,
                    });
                }

                this.oiPublisher.SaveKpiDefinition(definition);
            }

            Logger.WriteEndMessage(OssTracePoints.TelemetryTracePoints.DefineKpiIfRequiredEnd, TraceLayer.Telemetry, MethodBase.GetCurrentMethod().Name);
        }

        public void ServiceStart(IVssRequestContext systemRequestContext)
        {
            this.oiPublisher = new OIPublisher(systemRequestContext);
        }

        public void ServiceEnd(IVssRequestContext systemRequestContext)
        {

        }
    }
}
