using CloudStorageReaderLibrary.DataAccess;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace CloudStorageReaderLibrary
{
    public class TestExecutionAgentStripe : ReaderStripe<TestExecutionAgentLogEntry, TraceEvent>
    {
        public override void Initialize(ReaderChannel channel, int stripe)
        {
            base.Initialize(new ProductTraceSqlHelper(channel), channel, stripe);
        }

        public override IEnumerable<TraceEvent> DeserializeEntity(TestExecutionAgentLogEntry logEntry)
        {
            List<TraceEvent> events = new List<TraceEvent>();
            string logLevel = string.Empty;
            string computerName = string.Empty;
            string processName = string.Empty;
            string area = string.Empty;
            string method = string.Empty;
            int tracePoint = 0;
            int managedThreadId = 0;
            Guid sessionId = Guid.Empty;

            DateTime timeCreated = logEntry.Timestamp.UtcDateTime;

            try
            {
                string[] messageTokens = logEntry.Message.Split(new char[] { ',' }, 7, StringSplitOptions.None);
                string methodInfo;
                string[] ProcessInformation = null;
                string[] methodInfoTokens = null;

                int.TryParse(messageTokens[0], out tracePoint);

                // There can be trace messages from underlying TMI coming here. These messages have to be processed specially.
                if (tracePoint <= 0)
                {
                    // This is a TMI message. Let us assign a trace point 1252000 on the fly.
                    tracePoint = 1252000;
                    logLevel = messageTokens[0];
                    int.TryParse(messageTokens[2], out managedThreadId);
                    ProcessInformation = messageTokens[5].Split(new char[] { '\\' });
                    computerName = ProcessInformation[0];
                    processName = ProcessInformation[1];
                }
                else
                {
                    // Example log message
                    // "1250952, I, 038F0FBB-6A17-4878-A106-CFDBCEAC7BF0, 3976, 15, 2013/05/31 12:20:55.530, RD00155D462294/WaWorkerHost, CiEventService-PublishKpi-, Publishing KPI";
                    messageTokens = logEntry.Message.Split(new char[] { ',' }, 9, StringSplitOptions.None);
                    int.TryParse(messageTokens[4], out managedThreadId);
                    methodInfo = messageTokens[7];
                    logLevel = messageTokens[1];
                    if (!DateTime.TryParse(messageTokens[5].Trim(), out timeCreated))
                    {
                        timeCreated = logEntry.Timestamp.UtcDateTime;
                    }

                    methodInfoTokens = methodInfo.Split(new char[] { '-' });
                    area = methodInfoTokens[0];
                    method = methodInfoTokens[1];

                    ProcessInformation = messageTokens[6].Split(new char[] { '/' });
                    computerName = ProcessInformation[0];
                    processName = ProcessInformation[1];

                    Guid.TryParse(messageTokens[2], out sessionId);
                }
            }
            catch
            {
            }

            events.Add(new TraceEvent()
            {
                PartitionKey = logEntry.PartitionKey,
                RowKey = logEntry.RowKey,
                Tracepoint = tracePoint,
                Level = GetLogLevel(logLevel),
                ProcessId = logEntry.Pid,
                ThreadId = managedThreadId,
                Timestamp = logEntry.Timestamp.UtcDateTime,
                Service = logEntry.Role,
                ComputerName = computerName,
                ProcessName = processName,
                Area = area,
                Method = method,
                Message = logEntry.Message,
                TimeCreated = timeCreated,
                ActivityId = sessionId,
            });

            return events;
        }

        private static TraceLevel GetLogLevel(string level)
        {
            level = level.Trim();

            if (string.Equals(level, InfoShortName, StringComparison.OrdinalIgnoreCase))
            {
                return TraceLevel.Info;
            }
            if (string.Equals(level, WarningShortName, StringComparison.OrdinalIgnoreCase))
            {
                return TraceLevel.Warning;
            }
            if (string.Equals(level, ErrorShortName, StringComparison.OrdinalIgnoreCase))
            {
                return TraceLevel.Error;
            }

            return TraceLevel.Verbose;
        }

        private const string InfoShortName = "I";
        private const string WarningShortName = "W";
        private const string ErrorShortName = "E";
    }

    public class TestExecutionAgentLogEntry : TableEntryBase
    {
        public String Message { get; set; }
    }
}
