using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Microsoft.TeamFoundation.Common;

namespace Microsoft.TeamFoundation.Framework.Server
{
    #region LockingComponent - Dev10 schema

    internal class LockingComponent : FrameworkSqlResourceComponent
    {
        public static readonly ComponentFactory ComponentFactory = new ComponentFactory(new IComponentCreator[] {
            // Shipped in Dev10
            new ComponentCreator<LockingComponent>(1, isTransitionCreator: true),
            // Shipped in Dev11.Beta2
            new ComponentCreator<LockingComponent2>(2),
            new ComponentCreator<LockingComponent3>(3),
        }, ServiceNameConstants.Locking);

        public LockingComponent()
        {
        }

        protected override void Initialize(IVssRequestContext requestContext, string databaseCategory, int serviceVersion, ITFLogger logger)
        {
            // LockingComponent and LockingComponent2 work on a single tenant dbs. PartitionId must be set to 1.
            // We need to set PartitionId excplicitly, otherwise upgrade from Orcas will fail, 
            // because prc_QueryPartition sproc is not installed yet.
            if (serviceVersion < 3)
            {
                PartitionId = 1;
            }
        }

        /// <summary>
        /// Returns true if components supports AcquireLocks and ReleaseLocks methods.
        /// </summary>
        public virtual bool CanAcquireMultipleLocks
        {
            get { return false; }
        }

        /// <summary>
        /// Acquires the app lock for the resource with the given lock mode.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="lockMode"></param>
        /// <returns>True if the lock was granted successfully. False if the lock request timed out.</returns>
        public virtual bool AcquireLock(String resource, TeamFoundationLockMode lockMode, Int32 lockTimeout)
        {
            // SQL uses 0 for an infinite wait, we using Timeout.Infinite which
            // we need to adjust our timeout for. If the lockTimeout is shorter
            // than command timeout use the command timeout and the lock will
            // fail in the sproc faster.
            resource = AppendPartitionId(resource);
            Int32 sqlTimeout = lockTimeout;
            if (lockTimeout == Timeout.Infinite)
            {
                sqlTimeout = 0;
            }
            else
            {
                sqlTimeout = Math.Max(lockTimeout, CommandTimeout);
            }

            // This request is different from most, it should never timeout.
            // These connections must stay open until the lock is released.
            PrepareStoredProcedure("prc_AcquireLock", sqlTimeout);


            BindString("@lockMode", lockMode.ToString(), 32, false, System.Data.SqlDbType.NVarChar);
            BindString("@resource", resource, 255, false, System.Data.SqlDbType.NVarChar);
            BindInt("@lockTimeout", lockTimeout);

            //  0: The lock was successfully granted synchronously.
            //  1: The lock was granted successfully after waiting for other incompatible locks to be released.
            // -1: The lock request timed out.
            // Any other negative result is raised as an error in prc_AcquireLock.
            bool result = (Int32)ExecuteScalar() >= 0;

            // We need to release verification lock to ensure that we can perform schema updates while this component is holding a lock.
            ReleaseVerificationLock();

            return result;
        }

        /// <summary>
        /// Releases the lock for the given resource
        /// </summary>
        /// <param name="resource"></param>
        public virtual void ReleaseLock(String resource)
        {
            try
            {
                resource = AppendPartitionId(resource);
                PrepareStoredProcedure("prc_ReleaseLock");

                BindString("@resource", resource, 255, false, System.Data.SqlDbType.NVarChar);
                ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                TeamFoundationTracingService.TraceExceptionRaw(98000, s_area, s_layer, exception);
                throw;
            }
        }

        /// <summary>
        /// Queries the mode of the lock for the given resource.
        /// </summary>
        /// <param name="resource"></param>
        /// <returns></returns>
        public virtual TeamFoundationLockMode QueryLockMode(String resource)
        {
            resource = AppendPartitionId(resource);
            PrepareStoredProcedure("prc_QueryLockMode");

            BindString("@resource", resource, 255, false, System.Data.SqlDbType.NVarChar);

            ResultCollection rc = new ResultCollection(ExecuteReader(), "prc_QueryLockMode", RequestContext);
            rc.AddBinder<TeamFoundationLockMode>(new LockModeColumns());
            return rc.GetCurrent<TeamFoundationLockMode>().Items[0];
        }

        /// <summary>
        /// Updates connection string - sets connection pool size to 300
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        protected override ISqlConnectionInfo PrepareConnectionString(ISqlConnectionInfo connectionInfo)
        {
            // We are currently taking 3 lock when we are upgrading collection.
            // Default connection pool size is 100. We were running out of connections in the pool when we job agents was running ~30 jobs.
            ISqlConnectionInfo ret = connectionInfo.Clone();
            SqlConnectionStringBuilder connectionStringBuilder = new SqlConnectionStringBuilder(connectionInfo.ConnectionString);
            connectionStringBuilder.ApplicationName = "TFS Locking service";

            if (connectionStringBuilder.MaxPoolSize < 400)
            {
                connectionStringBuilder.MaxPoolSize = 400;
            }

            ret.ConnectionString = connectionStringBuilder.ConnectionString;
            return ret;
        }

        public virtual bool AcquireLocks(TeamFoundationLockMode lockMode, Int32 lockTimeout, String[] resources, out String timedoutLockName)
        {
            throw new InvalidServiceVersionException(ServiceNameConstants.Locking, 1, 2);
        }

        public virtual void ReleaseLocks(String[] resources)
        {
            try
            {
                throw new InvalidServiceVersionException(ServiceNameConstants.Locking, 1, 2);
            }
            catch (Exception exception)
            {
                TeamFoundationTracingService.TraceExceptionRaw(98001, s_area, s_layer, exception);
                throw;
            }
        }

        protected String AppendPartitionId(string resource)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0}:{1}", PartitionId, resource);
        }

        private static readonly String s_area = "Locking";
        private static readonly String s_layer = "LockingComponent";
    }

    #endregion

    internal class LockingComponent2 : LockingComponent
    {
        public LockingComponent2()
        {
        }

        /// <summary>
        /// Returns true if components supports AcquireLocks and ReleaseLocks methods.
        /// </summary>
        public override bool CanAcquireMultipleLocks
        {
            get { return true; }
        }

        /// <summary>
        /// Acquire one or more locks, in the order specified by resource names.
        /// </summary>
        /// <returns>True if all locks were acquired. False otherwise.</returns>
        public override bool AcquireLocks(TeamFoundationLockMode lockMode, Int32 lockTimeout, String[] resources, out String timedoutLockName)
        {
            resources = AppendPartitionIds(resources);

            // SQL uses 0 for an infinite wait, we using Timeout.Infinite which
            // we need to adjust our timeout for. If the lockTimeout is shorter
            // than command timeout use the command timeout and the lock will
            // fail in the sproc faster.
            Int32 sqlTimeout = lockTimeout;
            if (lockTimeout == Timeout.Infinite)
            {
                sqlTimeout = 0;
            }
            else
            {
                sqlTimeout = Math.Max(lockTimeout, CommandTimeout);
            }

            // This request is different from most, it should never timeout.
            // These connections must stay open until the lock is released.
            PrepareStoredProcedure("prc_AcquireLocks", sqlTimeout);

            BindString("@lockMode", lockMode.ToString(), 32, false, System.Data.SqlDbType.NVarChar);
            BindInt("@lockTimeout", lockTimeout);
            this.BindOrderedStringTable("@resources", resources);

            SqlDataReader reader = ExecuteReader();

            if (!reader.Read())
            {
                throw new UnexpectedDatabaseResultException(ProcedureName);
            }

            AcquireLocksColumns acquireLocksColumns = new AcquireLocksColumns();

            Int32 result = acquireLocksColumns.StatusColumn.GetInt32(reader);

            timedoutLockName = acquireLocksColumns.ResourceColumn.GetString(reader, allowNulls: true);

            // We need to release verification lock to ensure that we can perform schema updates while this component is holding a lock.
            ReleaseVerificationLock();

            //  0: The lock was successfully granted synchronously.
            //  1: The lock was granted successfully after waiting for other incompatible locks to be released.
            // -1: The lock request timed out.
            // Any other negative result is raised as an error in prc_AcquireLock.
            return result >= 0;
        }

        public override void ReleaseLocks(String[] resources)
        {
            try
            {
                resources = AppendPartitionIds(resources);
                PrepareStoredProcedure("prc_ReleaseLocks");

                this.BindOrderedStringTable("@resources", resources);
                ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                TeamFoundationTracingService.TraceExceptionRaw(98002, s_area, s_layer, exception);
                throw;
            }
        }

        private String[] AppendPartitionIds(String[] resources)
        {
            String[] partitionedResources = new String[resources.Length];
            for (int i = 0; i < resources.Length; i++)
            {
                partitionedResources[i] = AppendPartitionId(resources[i]);
            }
            return partitionedResources;
        }


        internal class AcquireLocksColumns
        {
            public SqlColumnBinder StatusColumn = new SqlColumnBinder("Status");
            public SqlColumnBinder LockIdColumn = new SqlColumnBinder("LockId");
            public SqlColumnBinder ResourceColumn = new SqlColumnBinder("Resource");
        }

        private static readonly String s_area = "Locking";
        private static readonly String s_layer = "LockingComponent2";
    }

    // LockingComponent3 does not have new methods, but it is used in multi-tenant dbs.
    // LockingComponent Initialize method do not explicitly initialize PartitionId in this case.
    internal class LockingComponent3 : LockingComponent2
    {
    }

    internal class LockModeColumns : ObjectBinder<TeamFoundationLockMode>
    {
        private readonly SqlColumnBinder lockModeColumn = new SqlColumnBinder("LockMode");

        protected override TeamFoundationLockMode Bind()
        {
            String stringValue = lockModeColumn.GetString(Reader, false);

            switch (stringValue)
            {
                case "NoLock" :
                    return TeamFoundationLockMode.NoLock;
                case "Shared" :
                    return TeamFoundationLockMode.Shared;
                case "Exclusive" :
                    return TeamFoundationLockMode.Exclusive;

                default :
                    Debug.Fail("Bad Sql Lock Mode " + stringValue);
                    return TeamFoundationLockMode.NoLock;
            }
        }
    }

}
