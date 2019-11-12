using System;

namespace Microsoft.TeamFoundation.Admin
{
    public enum DurationUnit
    {
        Minutes,
        Hours,
    }

    public enum BackupSetType
    {
        Unknown=0,
        Full,
        Transactional,
        Differential,
    }

    [Flags]
    public enum DatabaseCategory
    {
        None = 0,
        Collection = 1,
        Configuration = 2,
        Warehouse = 4,
        Analysis = 8,
        Reporting = 16,
        Sharepoint = 32,
        Tfs = Collection | Configuration | Warehouse,
        All = Collection | Configuration | Warehouse | Analysis | Reporting | Sharepoint
    }

    public enum BackupTaskExitCode
    {
        Success = 0,
        CompletedWithWarning,
        VerificationError,
        ConfigurationError,
        None,
    }

    public enum RenameCollectionState
    {
        CollectionNotFound,
        ServerNotConfigured,
        CollectionStopped,
        CollectionNotStopped,
    }
}
