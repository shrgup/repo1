// ----------------------------------------------------------------------------
// <copyright file="SourceImpoundActivity.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// ----------------------------------------------------------------------------

namespace Microsoft.VisualStudio.Services.OssEngineering.Activities
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using Microsoft.TeamFoundation.Framework.Server;
    using Microsoft.VisualStudio.Services.OssEngineering.Configuration;
    using Microsoft.VisualStudio.Services.OssEngineering.OssActions;
    using Microsoft.VisualStudio.Services.OssEngineering.LoggerPort;
    using Microsoft.VisualStudio.Services.OssEngineering.Telemetry;
    using Microsoft.VisualStudio.Services.OssEngineering.Utility;
    using Microsoft.VisualStudio.Services.OssEngineering.Jobs.Common;

    public class SourceImpoundActivity : IOssActivity
    {
        public string ActivityName
        {
            get
            {
                return "Source Impound";
            }
        }

        public void Execute(IVssRequestContext requestContext, IOssJobContext jobContext)
        {
            // Create local temperory directory for actions
            var oiPublishingService = requestContext.GetService<IOIPublishingService>();
            var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);
            var localContainerUri = new Uri(tempDirectory);

            var config = new SourceImpounderConfiguration(requestContext);
            var ossComponent = jobContext.GetOssComponent(requestContext);
            var projectUri = new Uri(
                string.Format(CultureInfo.InvariantCulture, "{0}/{1}/_git/{2}",
                config.AccountUri,
                ossComponent.ImpoundCollectionName,
                ossComponent.ImpoundProjectName));

            var sourcePort = jobContext.GetSourceControlPort(requestContext);
            var actions = new SourceControlActions(sourcePort);
            var manifestFileName = "Manifestfile";
            var author = config.ServiceUserName;

            // Actions to perform Source Impounding
            // Todo: Add retry logic per action based on exception.

             using (SimpleTimer timer = new SimpleTimer(requestContext, "Impounding Sources"))
            {
                // Initialize temporary deirectory
                jobContext.SaveActivitySubState(requestContext, "InitializeContainer");
                actions.InitializeRepository(localContainerUri);

                // Clone external repo locally
                 var impoundUri = new Uri(ossComponent.ExternalSourceUrl);
                jobContext.SaveActivitySubState(requestContext, "Pull");
                actions.Clone(new Uri(ossComponent.ExternalSourceUrl), localContainerUri);

                // Rename remote branches that are present locally with a prefix
                jobContext.SaveActivitySubState(requestContext, "RenameBranches");
                actions.RenameBranchesWithPrefix(localContainerUri, config.UpstreamBranchPrefix);

                // Create a topic branch in local repository
                jobContext.SaveActivitySubState(requestContext, "CreateTopicBranch");
                var topicBranchName = string.Empty;

                //TODO:Are children expanded here?
                if (ossComponent.Versions.Count() == 0)
                {
                    // if version is not specified, simply create the branch with the name
                    // Todo: get the latest tag or figure out the parent branch to create topic branch
                    topicBranchName = string.Format(
                        CultureInfo.InvariantCulture, 
                        "{0}latest_{1}", 
                        config.TopicBranchPrefix, 
                        DateTime.UtcNow.ToString("MM.dd.yyyy_HH.mm.ss.fff", CultureInfo.InvariantCulture));
                    actions.CreateBranch(localContainerUri, topicBranchName);
                }
                else
                {
                    // If the version is specified, create a branch on that version.
                    var tagName = ossComponent.Versions.ToList()[0].UpstreamTagName;
                    topicBranchName = string.Format(CultureInfo.InvariantCulture, "{0}{1}", config.TopicBranchPrefix, tagName);
                    actions.CreateBranch(localContainerUri, topicBranchName);
                }

                // Add manifest fest file to local repository
                jobContext.SaveActivitySubState(requestContext, "AddManifestFile");
                actions.AddManifestFile(localContainerUri, manifestFileName, topicBranchName, author);

                // Push local repository to VSO
                jobContext.SaveActivitySubState(requestContext, "Push");
                actions.Clone(localContainerUri, projectUri);

                oiPublishingService.PublishKpi(OssKpiEventName.SouceImpounderLatencyInSeconds, timer.TotalSecondsElapsed);
                oiPublishingService.PublishComponentSpecificCi(OssCiEventName.ExternalOssSource, ossComponent.Id, impoundUri.Host);
                oiPublishingService.PublishComponentSpecificCi(OssCiEventName.AccountId, ossComponent.Id, requestContext.ServiceHost.InstanceId.ToString());
                oiPublishingService.PublishComponentSpecificCi(OssCiEventName.ExternalOssSource, ossComponent.Id, ossComponent.ExternalProjectUrl);
            }

            try
            {
                var repoSize = GetDirectorySizeInMB(tempDirectory);
                oiPublishingService.PublishComponentSpecificCi(OssCiEventName.RepoSize, ossComponent.Id, repoSize);
                this.DeleteDirectory(tempDirectory);
            }
            catch (Exception ex)
            {
                Logger.WriteError(OssTracePoints.ServicesTracePoints.DeleteDirectoryError, TraceLayer.Services, ex);
            }
        }

        private double GetDirectorySizeInMB(string directory)
        {
            long sizeInBytes = 0;

            if (Directory.Exists(directory))
            {
                sizeInBytes = new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }

            return (sizeInBytes / (1024 * 1024));
        }

        private void DeleteDirectory(string directory)
        {
            var fileLists = from file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                            where ((File.GetAttributes(file) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            select file;

            foreach (var file in fileLists)
            {
                File.SetAttributes(file, File.GetAttributes(file) ^ FileAttributes.ReadOnly);
            }

            Directory.Delete(directory, true);
        }

    }
}
