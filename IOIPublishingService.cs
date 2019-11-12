// ----------------------------------------------------------------------------
// <copyright file="IOIPublishingService.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------
namespace Microsoft.VisualStudio.Services.OssEngineering.Telemetry
{
    using Microsoft.TeamFoundation.Framework.Server;

    [DefaultServiceImplementation(typeof(OIPublishingService))]
    public interface IOIPublishingService : IVssFrameworkService
    {
        void PublishKpi(OssKpiEventName eventName, double value);

        void PublishComponentSpecificCi(OssCiEventName eventName, int componentId, string value);

        void PublishComponentSpecificCi(OssCiEventName eventName, int componentId, double value);

        void PublishRequestSpecificCi(OssCiEventName eventName, int requestId, string value);
		
		void Print1();
	
    }

    public enum OssKpiEventName
    {
        NewRequest,
        FailedRequest,
        SouceImpounderLatencyInSeconds,
        SyncFailed,
        SyncLatencyInSeconds,
        RequestProcessingTimeInSeconds
    }

    public enum OssCiEventName
    {
        RepoSize,
        ExternalOssSource,
        ExternalProjectUrl,
        AccountId,
        CreatedBy,
        ComponentName,
        SyncLatencyInSecondUnit,        
   }
}