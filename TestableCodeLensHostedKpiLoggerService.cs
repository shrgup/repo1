using Microsoft.TeamFoundation.Framework.Server;
using MS.VS.Services.CodeLens.Platform.Hosted;
using System;
using System.Collections.Generic;

namespace Server.UnitTests
{
    internal class TestableCodeLensHostedKpiLoggerService : CodeLensHostedKpiLoggerService
    {
        public DateTime LastPublished
        {
            get
            {
                return base.lastPublished;
            }
            set
            {
                base.lastPublished = value;
            }
        }

        public int PublishEventsExecutionCount { get; set; }

        public Dictionary<Guid, Dictionary<string, Dictionary<string, double>>> AggregatedKpiValues
        {
            get
            {
                return base.aggregatedKpiValues;
            }
        }
		
		void Print();
		
        public override void PublishEvents(IVssRequestContext requestContext)
        {
            PublishEventsExecutionCount++;
            base.PublishEvents(requestContext);
        }

        protected override void PublishKpi(IVssRequestContext requestContext, string area, Guid hostId, string name, double value)
        {
            // Do nothing
        }

        protected override bool ShouldPublishKpiBatch(IVssRequestContext requestContext)
        {
            return DateTime.UtcNow.Subtract(LastPublished).Minutes >= 5;
        }
    }
}
