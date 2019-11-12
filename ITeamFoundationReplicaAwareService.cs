// Copyright (c) Microsoft Corporation.  All rights reserved.
using System;

namespace Microsoft.TeamFoundation.Framework.Server
{
    /// <summary>
    /// Interface for replication-aware Team Foundation services
    /// Service Management is about notification of startup and shutdown, and database instance affinity.
    /// </summary>
    public interface ITeamFoundationReplicaAwareService
    {
        /// <summary>
        /// Called when Host is being Shutdown and this service should free its resources
        /// </summary>
        /// <param name="requestContext">Request context that can be used to perform system actions</param>
        void ServiceEnd(IVssRequestContext requestContext);

        /// <summary>
        /// Called when this service is initialized
        /// </summary>
        /// <param name="requestContext">Request context that can be used to perform system actions</param>
        void ServiceStart(IVssRequestContext requestContext, bool isMaster);

        /// <summary>
        /// Database category the service is bound to
        /// </summary>
        string DatabaseCategory { get; }
    }
}
