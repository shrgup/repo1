using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
using Microsoft.TeamFoundation;
using Microsoft.TeamFoundation.Common;
using Microsoft.TeamFoundation.Framework.Server;
using Microsoft.VisualStudio.Services.Common;

// '########:'##::::'##:'####::'######:::::'####::'######:::::'##::: ##::'#######::'########:::::::'###::::
// ... ##..:: ##:::: ##:. ##::'##... ##::::. ##::'##... ##:::: ###:: ##:'##.... ##:... ##..:::::::'## ##:::
// ::: ##:::: ##:::: ##:: ##:: ##:::..:::::: ##:: ##:::..::::: ####: ##: ##:::: ##:::: ##::::::::'##:. ##::
// ::: ##:::: #########:: ##::. ######:::::: ##::. ######::::: ## ## ##: ##:::: ##:::: ##:::::::'##:::. ##:
// ::: ##:::: ##.... ##:: ##:::..... ##::::: ##:::..... ##:::: ##. ####: ##:::: ##:::: ##::::::: #########:
// ::: ##:::: ##:::: ##:: ##::'##::: ##::::: ##::'##::: ##:::: ##:. ###: ##:::: ##:::: ##::::::: ##.... ##:
// ::: ##:::: ##:::: ##:'####:. ######:::::'####:. ######::::: ##::. ##:. #######::::: ##::::::: ##:::: ##:
// :::..:::::..:::::..::....:::......::::::....:::......::::::..::::..:::.......::::::..::::::::..:::::..::
// '########::'########::::'###::::'##:::::::::::'######::'########:'########:'########::
//  ##.... ##: ##.....::::'## ##::: ##::::::::::'##... ##:... ##..:: ##.....:: ##.... ##:
//  ##:::: ##: ##::::::::'##:. ##:: ##:::::::::: ##:::..::::: ##:::: ##::::::: ##:::: ##:
//  ########:: ######:::'##:::. ##: ##::::::::::. ######::::: ##:::: ######::: ########::
//  ##.. ##::: ##...:::: #########: ##:::::::::::..... ##:::: ##:::: ##...:::: ##.....:::
//  ##::. ##:: ##::::::: ##.... ##: ##::::::::::'##::: ##:::: ##:::: ##::::::: ##::::::::
//  ##:::. ##: ########: ##:::: ##: ########::::. ######::::: ##:::: ########: ##::::::::
// ..:::::..::........::..:::::..::........::::::......::::::..:::::........::..:::::::::
// '########::'########:'########::'########::'#######::'########::'##::::'##:'########:'########::'####:
//  ##.... ##: ##.....:: ##.... ##: ##.....::'##.... ##: ##.... ##: ###::'###: ##.....:: ##.... ##: ####:
//  ##:::: ##: ##::::::: ##:::: ##: ##::::::: ##:::: ##: ##:::: ##: ####'####: ##::::::: ##:::: ##: ####:
//  ########:: ######::: ########:: ######::: ##:::: ##: ########:: ## ### ##: ######::: ########::: ##::
//  ##.....::: ##...:::: ##.. ##::: ##...:::: ##:::: ##: ##.. ##::: ##. #: ##: ##...:::: ##.. ##::::..:::
//  ##:::::::: ##::::::: ##::. ##:: ##::::::: ##:::: ##: ##::. ##:: ##:.:: ##: ##::::::: ##::. ##::'####:
//  ##:::::::: ########: ##:::. ##: ##:::::::. #######:: ##:::. ##: ##:::: ##: ########: ##:::. ##: ####:
// ..:::::::::........::..:::::..::..:::::::::.......:::..:::::..::..:::::..::........::..:::::..::....::

namespace Microsoft.VisualStudio.Services.Framework
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [StepPerformer("Database")]
    public class DatabaseStepPerformer : TeamFoundationStepPerformerBase
    {
        [ServicingStep]
        public void AddSqlRoleMember(ServicingContext servicingContext, AddSqlRoleMemberStepData stepData)
        {
            LogObjectProperties(servicingContext, stepData);

            ISqlConnectionInfo connectionInfo = servicingContext.GetConnectionInfo();

            if (string.Equals(stepData.Database, "Warehouse", StringComparison.Ordinal))
            {
                connectionInfo = servicingContext.GetItem<ISqlConnectionInfo>(ServicingItemConstants.WarehouseConnectionInfo);
                servicingContext.LogInfo("Using Warehouse connection string: {0}", ConnectionStringUtility.MaskPassword(connectionInfo.ConnectionString));
            }
            else if (!string.IsNullOrEmpty(stepData.Database))
            {
                // if they provided a database update the connection string
                servicingContext.LogInfo("Starting with connection string {0}", ConnectionStringUtility.MaskPassword(connectionInfo.ConnectionString));
                servicingContext.LogInfo("Replacing InitialCatalog with {0}", stepData.Database);

                String connectionString = new SqlConnectionStringBuilder(connectionInfo.ConnectionString)
                {
                    InitialCatalog = stepData.Database
                }.ConnectionString;

                connectionInfo = connectionInfo.Clone();

                connectionInfo.ConnectionString = connectionString;
            }

            using (TeamFoundationSqlSecurityComponent component = connectionInfo.CreateComponentRaw<TeamFoundationSqlSecurityComponent>(logger: servicingContext.TFLogger))
            {
                servicingContext.LogInfo("Using connection string {0}", ConnectionStringUtility.MaskPassword(connectionInfo.ConnectionString));
                servicingContext.LogInfo("About to grant windows account {0} access to the database as necessary and adding to database role {1}", stepData.Account, stepData.Role);

                component.ModifyExecRole(stepData.Account, stepData.Role, AccountsOperation.Add);
            }

            // Quick hack to fix P0 bug 678554. When we create TFSEXECROLE in master - GRANT it CREATE DATABASE and ALTER ANY USER permissions
            if (String.Equals(stepData.Database, "master", StringComparison.OrdinalIgnoreCase) &&
                String.Equals(stepData.Role, DatabaseRoles.TfsExecRole, StringComparison.OrdinalIgnoreCase))
            {
                using (SqlScriptResourceComponent masterComponent = connectionInfo.CreateComponentRaw<SqlScriptResourceComponent>(logger: servicingContext.TFLogger))
                {
                    masterComponent.ExecuteStatement("GRANT CREATE DATABASE TO TFSEXECROLE; GRANT ALTER ANY USER TO TFSEXECROLE", SqlCommandTimeout.FiveMinutes);
                }
            }
        }

        /// <summary>
        /// This step creates the physical configuration database.
        /// </summary>
        [ServicingStep]
        public void CreateDatabase(ServicingContext servicingContext, CreateDatabaseStepData stepData)
        {
            ISqlConnectionInfo dataTierConnectionInfo = SqlConnectionInfoFactory.Create(servicingContext.Tokens[ServicingTokenConstants.DataTierConnectionString]);
            servicingContext.LogInfo("Data tier connection string: {0}", ConnectionStringUtility.MaskPassword(dataTierConnectionInfo.ConnectionString));

            ISqlConnectionInfo connectionInfo = servicingContext.GetConnectionInfo();

            SqlConnectionStringBuilder connectionBuilder = new SqlConnectionStringBuilder(connectionInfo.ConnectionString);
            string databaseName = connectionBuilder.InitialCatalog;

            ISqlConnectionInfo dbdtConnectionInfo = dataTierConnectionInfo.Clone();
            SqlConnectionStringBuilder dataTierConnectionBuilder = new SqlConnectionStringBuilder(dbdtConnectionInfo.ConnectionString);
            dataTierConnectionBuilder.InitialCatalog = databaseName;

            // To fix bug 348288 (http://vstspioneer:8080/tfs/web/wi.aspx?pcguid=2e80e4ed-2a78-4655-86d9-026c3c253504&id=348288)
            // we will increase connect timeout from default 15 seconds to 2 minutes.
            dataTierConnectionBuilder.ConnectTimeout = 120;

            dbdtConnectionInfo.ConnectionString = dataTierConnectionBuilder.ConnectionString;

            // See if the database already exists.
            bool databaseExists;
            using (TeamFoundationDataTierComponent masterComponent = dataTierConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
            {
                databaseExists = masterComponent.CheckIfDatabaseExists(databaseName);
            }

            if (databaseExists)
            {
                // Confirm the existing database is empty.
                using (TeamFoundationDataTierComponent dbComponent = dbdtConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>())
                {
                    bool isEmpty = dbComponent.CheckIfDatabaseIsEmpty();

                    if (!isEmpty)
                    {
                        throw new DatabaseAlreadyExistsException(databaseName, dataTierConnectionInfo.DataSource);
                    }
                }
            }
            else
            {
                // Create a new database.
                using (TeamFoundationDataTierComponent masterComponent = dataTierConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
                {
                    masterComponent.CreateDatabase(databaseName, collation: null);

                    // Resize the database if we're on Azure.
                    if (masterComponent.IsSqlAzure && stepData.MaxSizeInGB > 1)
                    {
                        servicingContext.LogInfo("Changing max database size to {0} GB. Database name: {1}.", stepData.MaxSizeInGB, databaseName);
                        masterComponent.AlterSqlAzureDatabaseMaxSize(databaseName, stepData.MaxSizeInGB);
                    }
                }
            }

            // Sql Azure, devfabric - make sure that config database SQL Login exists
            if (!connectionBuilder.IntegratedSecurity)
            {
                servicingContext.LogInfo("Verifying login.");
#pragma warning disable 0618
                //need to get user name an password in plain text to verify
                String insecureConnectionString = connectionInfo.GetFullConnectionStringInsecure();
#pragma warning restore 0618
                SqlConnectionStringBuilder verifyBuilder = new SqlConnectionStringBuilder(insecureConnectionString);
                verifyBuilder.Remove("Initial Catalog");
                if (!VerifyLogin(servicingContext, dataTierConnectionInfo, verifyBuilder))
                {
                    // The login was not valid.
                    return;
                }
            }

            using (TeamFoundationSqlSecurityComponent component = dbdtConnectionInfo.CreateComponentRaw<TeamFoundationSqlSecurityComponent>())
            {
                component.ProvisionTfsRoles();

                // Sql Azure, devfabric
                if (!connectionBuilder.IntegratedSecurity)
                {
                    string userName = component.CreateUser(((ISupportSqlCredential)connectionInfo).UserId);
                    component.AddRoleMember(DatabaseRoles.TfsExecRole, userName);
                }
                // In on-premises TFS AddSqlRoleMember step performers create logins and database users for service account
            }
        }

        private bool VerifyLogin(ServicingContext servicingContext, ISqlConnectionInfo dataTierConnectionInfo, SqlConnectionStringBuilder verifyConnectionString, bool createLogin = true)
        {
            servicingContext.LogInfo("Validating login '{0}'. Create User if it does not exist: {1}", verifyConnectionString.UserID, createLogin);

            bool success = false;
            try
            {
                using (TeamFoundationSqlSecurityComponent masterDbSecurityComponent = dataTierConnectionInfo.CreateComponentRaw<TeamFoundationSqlSecurityComponent>(logger: servicingContext.TFLogger))
                {
                    SqlLoginInfo login = masterDbSecurityComponent.GetLogin(verifyConnectionString.UserID);

                    if (login == null)
                    {
                        if (createLogin)
                        {
                            servicingContext.LogInfo("Login '{0}' not found. Creating a login.", verifyConnectionString.UserID);
                            masterDbSecurityComponent.CreateSqlAuthLogin(verifyConnectionString.UserID, verifyConnectionString.Password);
                            success = true;
                        }
                        else
                        {
                            servicingContext.Error(string.Format("Login '{0}' not found.", verifyConnectionString.UserID));
                        }
                    }
                    else
                    {
                        servicingContext.LogInfo("Login '{0}' already exists. Updating password.", login.LoginName);
                        masterDbSecurityComponent.AlterSqlLoginPassword(login.LoginName, verifyConnectionString.Password);
                        success = true;
                    }

                    if(!masterDbSecurityComponent.IsSqlAzure)
                    {
                        masterDbSecurityComponent.GrantViewServerStatePermission(verifyConnectionString.UserID);
                    }
                }
            }
            catch (DatabaseConfigurationException)
            {
                servicingContext.LogInfo("Failed to connect to the database to determine if the user was valid. Connection string: {0}", ConnectionStringUtility.MaskPassword(dataTierConnectionInfo.ConnectionString));
            }

            return success;
        }

        /// <summary>
        /// Creates a physical database for collection in on-premises TFS. This step cannot be used on hosted TFS.
        /// CreateCollectionDatabaseStepData has DatabaseName and SqlServerInstance properties,
        /// which specify database name and and SQL Server instance on which the database should be hosted.
        /// </summary>
        /// <param name="servicingContext"></param>
        [ServicingStep]
        private void CreateCollectionDatabase(ServicingContext servicingContext, CreateCollectionDatabaseStepData data)
        {
            IVssRequestContext deploymentRequestContext = servicingContext.DeploymentRequestContext;

            Debug.Assert(deploymentRequestContext != null, "No request context");
            Debug.Assert(deploymentRequestContext.ServiceHost.Is(TeamFoundationHostType.Deployment), "Request context is not a deployment context");
            Debug.Assert(deploymentRequestContext.ExecutionEnvironment.IsOnPremisesDeployment, "This step is not supported on hosted TFS.");

            servicingContext.LogInfo("Database name: {0}, Sql Server Instance: {1}", data.DatabaseName, data.SqlServerInstance);

            SqlConnectionStringBuilder dcs = new SqlConnectionStringBuilder(deploymentRequestContext.FrameworkConnectionInfo.ConnectionString);

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = data.SqlServerInstance,
                IntegratedSecurity = true
            };

            ISqlConnectionInfo dataTierConnectionInfo = SqlConnectionInfoFactory.Create(builder.ConnectionString);

            string collectionConnectionString = ConnectionStringUtility.ReplaceInitialCatalog(dataTierConnectionInfo.ConnectionString, data.DatabaseName);

            TeamFoundationDatabaseManagementService dbms =
                deploymentRequestContext.GetService<TeamFoundationDatabaseManagementService>();

            ITeamFoundationDatabaseProperties databaseProperties = dbms.CreateDatabase(deploymentRequestContext,
                data.DatabaseName,
                null, // collation
                DatabaseManagementConstants.DefaultPartitionPoolName, // pool
                dataTierConnectionInfo,
                TeamFoundationDatabaseType.Partition,
                servicingContext.TFLogger);

            ISqlConnectionInfo configDbConnectionInfo = servicingContext.DeploymentRequestContext.FrameworkConnectionInfo;
            String serviceLevel;

            using (ExtendedAttributeComponent configComponent = configDbConnectionInfo.CreateComponentRaw<ExtendedAttributeComponent>())
            {
                serviceLevel = configComponent.ReadServiceLevelStamp();
            }

            if (String.IsNullOrEmpty(serviceLevel))
            {
                throw new TeamFoundationServicingException(CoreServicingResources.ConfigurationDatabaseServiceLevelNotFound());
            }

            // Set the DatabaseId in the servicing context
            servicingContext.Items[ServicingItemConstants.CollectionDatabaseId] = databaseProperties.DatabaseId; // Different steps look for databaseId in different places. Accounts vs Collections.
            TeamProjectCollectionProperties collectionProperties = servicingContext.GetItem<TeamProjectCollectionProperties>(ServicingItemConstants.CollectionProperties);
            collectionProperties.DatabaseId = databaseProperties.DatabaseId;
        }

        /// <summary>
        /// Drop database if it exists.
        /// </summary>
        [ServicingStep]
        public void DropDatabase(ServicingContext servicingContext, DropDatabaseStepData stepData)
        {
            servicingContext.LogInfo("Drop database               : {0}", stepData.Drop);
            servicingContext.LogInfo("Database Name               : {0}", stepData.DatabaseName);
            servicingContext.LogInfo("Data tier connection string : {0}",
                ConnectionStringUtility.MaskPassword(stepData.DataTierConnectionString));

            ISqlConnectionInfo dataTierConnectionInfo = SqlConnectionInfoFactory.Create(stepData.DataTierConnectionString);

            if (stepData.Drop)
            {
                using (TeamFoundationDataTierComponent component = dataTierConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
                {
                    servicingContext.LogInfo("Checking if database exists.");
                    bool databaseExists = component.CheckIfDatabaseExists(stepData.DatabaseName);
                    servicingContext.LogInfo("Database exists: {0}", databaseExists);

                    if (databaseExists)
                    {
                        servicingContext.LogInfo("Dropping database: {0}", stepData.DatabaseName);
                        component.DropDatabase(stepData.DatabaseName, DropDatabaseOptions.CloseExistingConnections);
                        servicingContext.LogInfo("Database has been dropped");
                    }
                }
            }
        }

        [ServicingStep]
        public void DropBuildDatabase(IVssRequestContext deploymentContext, ServicingContext servicingContext)
        {
            TeamFoundationRegistryService registryService = deploymentContext.GetService<TeamFoundationRegistryService>();

            const string buildConnectionStringRegistryPath = "/Configuration/Database/Build/ConnectionString";
            string buildConnectionString = registryService.GetValue(deploymentContext, buildConnectionStringRegistryPath);

            if (!string.IsNullOrEmpty(buildConnectionString))
            {
                TeamFoundationDataTierService dataTierService = deploymentContext.GetService<TeamFoundationDataTierService>();

                string buildDatabaseName = TeamFoundationDataTierService.GetDatabaseName(buildConnectionString);

                servicingContext.LogInfo("Build database: {0}", buildDatabaseName);

                string configurationDatabaseName = TeamFoundationDataTierService.GetDatabaseName(deploymentContext.FrameworkConnectionInfo.ConnectionString);

                // We don't want to drop config db if build logical database resides in the same physical database as Framework.
                if (!VssStringComparer.DatabaseName.Equals(configurationDatabaseName, buildDatabaseName))
                {
                    using (TeamFoundationLock dtLock = dataTierService.AcquireSharedLock(deploymentContext))
                    {
                        DataTierInfo dtInfo = dataTierService.FindAssociatedDataTier(deploymentContext, buildConnectionString);

                        using (TeamFoundationDataTierComponent component = dtInfo.ConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
                        {
                            bool databaseExists = component.CheckIfDatabaseExists(buildDatabaseName);

                            if (databaseExists)
                            {
                                component.DropDatabase(buildDatabaseName, DropDatabaseOptions.CloseExistingConnections);
                            }
                        }
                    }
                }

                registryService.DeleteEntries(deploymentContext, buildConnectionStringRegistryPath);
            }
            else
            {
                servicingContext.LogInfo("Build connection string not found");
            }
        }

        /// <summary>
        /// Drops all databases that belong to hosted Tfs/Sps deployment as well as SQL Azure logins that are used to connect to at least one of the databases.
        /// </summary>
        [ServicingStep]
        public void DropDeploymentDatabases(ServicingContext servicingContext, DropDeploymentDatabasesStepData stepData)
        {
            servicingContext.LogInfo("Drop database               : {0}", stepData.Drop);
            servicingContext.LogInfo("Config db connection string : {0}", ConnectionStringUtility.MaskPassword(stepData.ConfigDbConnectionString));
            servicingContext.LogInfo("Data tier connection string : {0}", ConnectionStringUtility.MaskPassword(stepData.DataTierConnectionString));
            servicingContext.LogInfo("Ops connection string       : {0}", ConnectionStringUtility.MaskPassword(stepData.OpsConnectionString));

            if (!stepData.Drop)
            {
                return;
            }

            ISqlConnectionInfo configDbConnectionInfo = SqlConnectionInfoFactory.Create(stepData.ConfigDbConnectionString);
            ISqlConnectionInfo dataTierConnectionInfo = SqlConnectionInfoFactory.Create(stepData.DataTierConnectionString);
            ISqlConnectionInfo opsConnectionInfo = AdminConnectionInfoHelper.Create(servicingContext, stepData.OpsConnectionString);
            string configDbName = configDbConnectionInfo.InitialCatalog;
            List<ISqlConnectionInfo> connectionInfoList = new List<ISqlConnectionInfo> () { dataTierConnectionInfo };

            bool isSqlAzure;
            using (TeamFoundationDataTierComponent dataTierComponent = opsConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
            {
                if (!dataTierComponent.CheckIfDatabaseExists(configDbName))
                {
                    servicingContext.LogInfo("Configuration database does not exist.");
                    return;
                }
                isSqlAzure = dataTierComponent.IsSqlAzure;
            }

            if (!isSqlAzure)
            {
                // Build windows authentication connection string in case if dataTierConnectionString fails to drop database (devfabric only)
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(stepData.OpsConnectionString);
                builder.Remove("User ID");
                builder.Remove("Password");
                builder.IntegratedSecurity = true;
                ISqlConnectionInfo windowsAuthConnectionInfo = SqlConnectionInfoFactory.Create(builder.ConnectionString);
                connectionInfoList.Add(windowsAuthConnectionInfo);
            }

            // Verify that the TfsCfg user is valid (if it exists).
            SqlConnectionStringBuilder verifyBuilder = new SqlConnectionStringBuilder(stepData.ConfigDbConnectionString);
            verifyBuilder.Remove("Initial Catalog");
            if (!VerifyLogin(servicingContext, opsConnectionInfo, verifyBuilder, createLogin: false))
            {
                // The login did not exist or the password was not correct. We support multiple deployments in the same Sql instance for Test purposes, so we can't alter or drop the user.
                servicingContext.Error(string.Format("The login '{0}' was not valid, or it may be in use by another deployment. This step cannot determine if the existing databases should be dropped. Please verify and manually drop existing deployment databases and this login.", verifyBuilder.UserID));
                return;
            }

            // Remove sql credential from the cache before we drop the database
            SqlConnectionStringBuilder dtBuilder = new SqlConnectionStringBuilder(stepData.DataTierConnectionString);
            SqlConnectionInfoFactory.RemoveSqlCredentialFromCache(dtBuilder.DataSource, dtBuilder.UserID);

            List<string> logins = new List<string>();
            List<string> parsedLogins = new List<string>();

            // Get login names for use after dropping databases
            try
            {
                using (SqlScriptResourceComponent sqlComponent = configDbConnectionInfo.CreateComponentRaw<SqlScriptResourceComponent>())
                {
                    using (SqlDataReader reader = sqlComponent.ExecuteStatementReader("SELECT UserId FROM	tbl_DatabaseCredential"))
                    {
                        while (reader.Read())
                        {
                            logins.Add(reader.GetString(0));
                        }
                    }
                }
            }
            catch (TeamFoundationServicingException)
            {
                servicingContext.Warn("Querying configuration database crendentials failed. Dropping database will continue, but the logins may not be removed.");
            }

            // get prefix string
            string prefix = string.Empty;
            if (configDbName.EndsWith("Configuration"))
            {
                prefix = configDbName.Remove(configDbName.LastIndexOf('C'));
            }
            else
            {
                servicingContext.Error("Configuration database name is not correct.");
                return;
            }

            List<string> databaseNames;
            List<string> parsedDbNames = new List<string>();
            // Connect to datatier and remove the deployment databases
            using (TeamFoundationDataTierComponent dataTierComponent = dataTierConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
            {
                ResultCollection dbNameCollection = dataTierComponent.GetDatabaseNames();
                databaseNames = dbNameCollection.GetCurrent<string>().Items;
            }

            foreach (string db in databaseNames)
            {
                // The database name should be in the format of "<prefix>_[optionalText][_]<Guid in string format>"
                if (db.StartsWith(prefix))
                {
                    Guid parsedGuid;
                    string suffix = db.Substring(prefix.Length);
                    // The "_" is optional
                    if (suffix.StartsWith("_"))
                    {
                        suffix = suffix.Substring(1);
                    }
                    //If we can parse the Guid, we got the right database we need to drop
                    if (Guid.TryParse(suffix, out parsedGuid))
                    {
                        parsedDbNames.Add(db);
                    }
                }
            }

            DropDatabases(servicingContext, connectionInfoList, parsedDbNames);

            // Drop logins (best effort)
            foreach (string login in logins)
            {
                Guid parsedGuid;
                // User logins are in the format of "<prefix>-GUID (32 hex code)"
                if (Guid.TryParse(login.Substring(login.LastIndexOf('-') + 1), out parsedGuid))
                {
                    parsedLogins.Add(login);
                }
            }
            DropLogins(servicingContext, dataTierConnectionInfo, parsedLogins);

            // Drop configuration DB
            if (servicingContext.GroupResolution != TeamFoundation.Framework.Common.ServicingStepState.Failed)
            {
                servicingContext.LogInfo("Dropped configuration database: {0}", configDbName);
                DropDatabases(servicingContext, connectionInfoList, new string[] { configDbName });
            }
            else
            {
                servicingContext.Warn("Did not drop the configuration database because we failed to drop the partition db.");
            }
        }

        [ServicingStep]
        public void DropUserDatabases(ServicingContext servicingContext, DropUserDatabasesStepData stepData)
        {
            ISqlConnectionInfo opsConnectionInfo = AdminConnectionInfoHelper.Create(servicingContext, stepData.OpsConnectionString);
            servicingContext.LogInfo("Ops connection string : {0}", ConnectionStringUtility.MaskPassword(opsConnectionInfo.ConnectionString));

            List<String> systemDatabaseNames = new List<String>()
            {
                "master",
                "tempdb",
                "model",
                "msdb"
            };

            using (TeamFoundationDataTierComponent dataTierComponent = opsConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
            {
                dataTierComponent.GetDatabaseNames().GetCurrent<String>().Items.Except(systemDatabaseNames, StringComparer.OrdinalIgnoreCase).ToList().ForEach(db =>
                    {
                        servicingContext.LogInfo("Dropping Database [{0}]", db);
                        DropDatabase(servicingContext, dataTierComponent, db, dataTierComponent.DataSource);
                    });

                using (TeamFoundationSqlSecurityComponent securityComponent = opsConnectionInfo.CreateComponentRaw<TeamFoundationSqlSecurityComponent>())
                {
                    String userId = String.Empty;
                    if (opsConnectionInfo is ISupportSqlCredential)
                    {
                        userId = ((ISupportSqlCredential)opsConnectionInfo).UserId;
                    }

                    List<String> exceptionListForSqlUsersAndLogins = new List<String>()
                    {
                        userId, // Ops connection string user (usually tfsazureops)
                        "dbo",
                        "sa" // system administrator for on-premises (gsaa)
                    };

                    List<String> toDelete = dataTierComponent.GetDatatierUsers().Except(exceptionListForSqlUsersAndLogins, StringComparer.OrdinalIgnoreCase).ToList();
                    toDelete.ForEach(user =>
                    {
                        servicingContext.LogInfo("Dropping User [{0}]", user);
                        try
                        {
                            securityComponent.DropDatabaseUser(user);
                        }
                        catch (SqlException e)
                        {
                            servicingContext.Error(string.Format("Failed to drop user {0}. {1}", user, e.Message));
                        }
                    });

                    toDelete = dataTierComponent.GetDatatierLogins().Except(exceptionListForSqlUsersAndLogins, StringComparer.OrdinalIgnoreCase).ToList();
                    toDelete.ForEach(login =>
                        {
                            servicingContext.LogInfo("Dropping Login [{0}]", login);
                            try
                            {
                                securityComponent.DropLogin(login);
                            }
                            catch (SqlException e)
                            {
                                servicingContext.Error(string.Format("Failed to drop login {0}. {1}", login, e.Message));
                            }
                        });
                }
            }
        }

        [ServicingStep]
        public void EnablePrefixCompression(ServicingContext servicingContext)
        {
            ISqlConnectionInfo connectionInfo = servicingContext.GetConnectionInfo();

            using (FrameworkServicingComponent component = connectionInfo.CreateComponentRaw<FrameworkServicingComponent>(logger: servicingContext.TFLogger))
            {
                component.EnablePrefixCompression();
            }
        }


        /// <summary>
        /// Renames the configuration database to Tfs_Configuration(Failed{Number}) if it exists and has TFS_CONFIRATION_IN_PROGRESS and 
        /// user did not choose to use existing empty configuration database.
        /// If there are more than 10 databases with Tfs_Configuration(Failed{Number}) name, the name will be Tfs_Configuration(Failed{Guid}).
        /// The step fails if the database does not have TFS_CONFIRATION_IN_PROGRESS stamp or offline/unavailable.
        /// </summary>
        [ServicingStep]
        public void RenameFailedConfigurationDatabase(ServicingContext servicingContext)
        {
            bool useExisting = false;

            string existing;
            servicingContext.TryGetToken(ServicingTokenConstants.ExistingEmpty, out existing);
            useExisting = string.Equals(existing, "true", StringComparison.OrdinalIgnoreCase);

            if (!useExisting)
            {
                ISqlConnectionInfo connectionInfo = servicingContext.GetConnectionInfo();
                SqlConnectionStringBuilder connectionBuilder = new SqlConnectionStringBuilder(connectionInfo.ConnectionString);

                ISqlConnectionInfo masterConnectionInfo = connectionInfo.Clone();

                string databaseName = connectionBuilder.InitialCatalog;
                servicingContext.LogInfo("Database name: {0}, SQL Instance: {1}", databaseName, connectionBuilder.DataSource);

                connectionBuilder.InitialCatalog = "master";
                masterConnectionInfo.ConnectionString = connectionBuilder.ConnectionString;
                connectionBuilder.InitialCatalog = databaseName;

                if (CheckIfDatabaseExists(servicingContext, masterConnectionInfo, databaseName))
                {
                    servicingContext.LogInfo(c_databaseExistsFormat, databaseName);
                    // Rename the database if it has TFS_CONFIGURATION_IN_PROGRESS stamp. Presence of TFS_CONFIGURATION_IN_PROGRESS stamp could mean 2 things: 
                    // the database is being created by other AT or configuration failed and customer is re-trying to configure TFS. 
                    // It is extreamly unlikely that someone is creating the configuration database on the same SQL Instance, but there is a chance that provisioning of 
                    // configuration database fails. To make re-tring less painful, we will rename old database.

                    // If TFS_CONFIGURATION_IN_PROGRESS stamp does not exist or we failed to read it (database is offline for example), 
                    // this step will fail.
                    string configurationInProgressStamp = null;

                    try
                    {
                        using (ExtendedAttributeComponent component = connectionInfo.CreateComponentRaw<ExtendedAttributeComponent>())
                        {
                            configurationInProgressStamp = component.ReadDatabaseAttribute(ExtendedAttributeComponent.ExtendedPropertyConfigurationInProgressStamp);
                        }
                    }
                    catch (Exception ex)
                    {
                        servicingContext.LogInfo(ex.ToString());
                    }

                    if (string.IsNullOrEmpty(configurationInProgressStamp))
                    {
                        servicingContext.Error(CoreServicingResources.DatabaseAlreadyExists(databaseName,
                                            connectionBuilder.DataSource));
                        return;
                    }

                    string newDatabaseName = GenerateNameForFailedConfigDatabase(servicingContext, connectionInfo, databaseName);
                    servicingContext.LogInfo("Renaming '{0}' database to '{1}'. SQL Instance: {2}.", databaseName, newDatabaseName, connectionBuilder.DataSource);

                    using (TeamFoundationDataTierComponent component = masterConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
                    {
                        newDatabaseName = component.RenameDatabase(databaseName, newDatabaseName);
                    }

                    servicingContext.LogInfo("'{0}' database has been renamed to '{1}'. SQL Instance: {2}.", databaseName, newDatabaseName, connectionBuilder.DataSource);
                }
            }
        }

        /// <summary>
        /// Sets read committed snapshot isolation ON or OFF on the specified database.
        /// </summary>
        [ServicingStep]
        public void SetRcsi(ServicingContext servicingContext, SetRcsiStepData stepData)
        {
            string scriptName = stepData.Enable ? "TurnOnRCSI.sql" : "TurnOffRCSI.sql";

            ISqlConnectionInfo connectionInfo = servicingContext.GetConnectionInfo();

            using (SqlScriptResourceComponent component = connectionInfo.CreateComponentRaw<SqlScriptResourceComponent>(logger: servicingContext.TFLogger))
            {
                // RCSI is always enabled on Sql Azure. It is not possible to turn off RCSI.
                if (component.IsSqlAzure)
                {
                    return;
                }

                SqlScript sqlScript = new SqlScript(scriptName, servicingContext.ResourceProvider.GetServicingResource(scriptName));
                component.ExecuteScript(sqlScript);
            }

            connectionInfo = connectionInfo.Clone();
            SqlConnectionStringBuilder connectionBuilder = new SqlConnectionStringBuilder(connectionInfo.ConnectionString);
            connectionBuilder.Pooling = false;
            connectionBuilder.ConnectTimeout = 120;
            connectionInfo.ConnectionString = connectionBuilder.ConnectionString;

            // Make sure that we can connect to the database after we switched RCSI.
            // We have 10 SQM failures in less than 2 weeks after shipping TFS2010 RC, 
            // where upgrade/configuraration failed because 
            // we could not connect to the database right after we turned ON RCSI.
            //
            // We will try to connect to the db for up to 3 minutes.
            Stopwatch stopWatch = Stopwatch.StartNew();

            while (true)
            {
                try
                {
                    using (ExtendedAttributeComponent component = connectionInfo.CreateComponentRaw<ExtendedAttributeComponent>())
                    {
                        component.ReadDatabaseAttribute("UNKNOWN_ATTRIBUTE");
                    }

                    servicingContext.LogInfo("Querying database state");

                    ISqlConnectionInfo masterConnectionInfo = connectionInfo.Clone();
                    string masterConnectionString = new SqlConnectionStringBuilder(connectionInfo.ConnectionString)
                    {
                        InitialCatalog = "master"
                    }.ConnectionString;
                    masterConnectionInfo.ConnectionString = masterConnectionString;

                    DatabaseInformation dbInfo;

                    using (TeamFoundationDataTierComponent dtComponent = masterConnectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
                    {
                        dbInfo = dtComponent.GetDatabaseInfo(connectionBuilder.InitialCatalog);
                    }

                    if (dbInfo != null)
                    {
                        servicingContext.LogInfo("Collation: {0}, Compatibility level: {1}, Create date: {2}, Full Text Enabled: {3}, State: {4}, Access: {5}",
                            dbInfo.Collation,
                            dbInfo.CompatibilityLevel,
                            dbInfo.CreateDate,
                            dbInfo.FullTextEnabled,
                            dbInfo.State,
                            dbInfo.UserAccess);

                        if (dbInfo.State != DatabaseState.Online)
                        {
                            if (stopWatch.Elapsed < s_databaseConnectionFailureMaxWait)
                            {
                                Thread.Sleep(s_databaseConnectionFailureRetryPause);
                                continue;
                            }
                        }
                    }

                    long pageCount;
                    using (SqlScriptResourceComponent component = connectionInfo.CreateComponentRaw<SqlScriptResourceComponent>(logger: servicingContext.TFLogger))
                    {
                        pageCount = (long)component.ExecuteStatementScalar("SELECT SUM(reserved_page_count) FROM sys.dm_db_partition_stats");
                    }

                    // 1 page - 8KB
                    // 128 pages - 1MB
                    long sizeInMB = pageCount / 128;

                    servicingContext.LogInfo("Database size: {0} MB", sizeInMB);

                    if (sizeInMB > 1024 * 1024)
                    {
                        servicingContext.LogInfo("Database is larger than 1 TB. Sleeping for 30 seconds");
                        Thread.Sleep(TimeSpan.FromSeconds(30));
                    }
                    else if (sizeInMB > 1024)
                    {
                        servicingContext.LogInfo("Database is larger than 1 GB. Sleeping for 5 seconds");
                        Thread.Sleep(TimeSpan.FromSeconds(5));
                    }
                    else if (stepData.Enable)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }

                    break;
                }
                catch (DatabaseConnectionException ex)
                {
                    try
                    {
                        servicingContext.LogInfo(ex.ToString());
                    }
                    catch
                    {
                        // Ignore log errors. Trying to think of a scenario, when we are
                        // logging to the same database as the one we switched RCSI on.
                        // Probably this scenario does not exist. In any case, this step succeed already, because
                        // we set RCSI successfully.
                    }

                    if (stopWatch.Elapsed < s_databaseConnectionFailureMaxWait)
                    {
                        Thread.Sleep(s_databaseConnectionFailureRetryPause);
                    }
                    else
                    {
                        // We tried to connect to db and all attempts failed. Just return - technically this step succeeded, because it switched RCSI.
                        break;
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        servicingContext.LogInfo(ex.ToString());
                    }
                    catch
                    {
                        // Ignoring log errors. 
                    }

                    // We should never hit this line of code, but if we got here - just return.
                    // Technically the step succeeded - it switched RCSI.
                    break;
                }
            }
        }

        /// <summary>
        /// Sets up an offline file group and offline partition, which we will run on dev/test machine to detect failed conditions.
        /// </summary>
        [ServicingStep]
        public void SetupConfigDbTestPartition(ServicingContext servicingContext)
        {
            ISqlConnectionInfo frameworkConnectionInfo = servicingContext.GetConnectionInfo();

            string setupTestPartitionToken;
            bool setupTestPartition = false;

            if (servicingContext.TryGetToken(ServicingTokenConstants.SetupTestPartition, out setupTestPartitionToken))
            {
                setupTestPartition = string.Equals(setupTestPartitionToken, "True", StringComparison.OrdinalIgnoreCase);

                if (setupTestPartition)
                {
                    RegistryEntry[] entries = new RegistryEntry[1];
                    entries[0] = new RegistryEntry("#" + FrameworkServerConstants.ServicingSetupTestPartition, "True");

                    using (RegistryComponent registryComponent = frameworkConnectionInfo.CreateComponentRaw<RegistryComponent>(180, 200, 20))
                    {
                        registryComponent.UpdateRegistry(DatabasePartitionConstants.DeploymentHostId, string.Empty, entries);
                    }

                    ISqlConnectionInfo connectionInfo = servicingContext.GetConnectionInfo();
                    using (TestPartitionComponent component = connectionInfo.CreateComponentRaw<TestPartitionComponent>(commandTimeout:0))
                    {
                        component.SetupTestPartition();
                    }

                    // Give time for offline partition to complete.  Make sure we allow multiple connections before moving on.
                    System.Threading.Thread.Sleep(1000);

                    servicingContext.LogInfo("Clearing all SqlConnection pools.");
                    SqlConnection.ClearAllPools();
                }
            }
        }

        /// <summary>
        /// Sets up an offline file group and offline partition, which we will run on dev/test machine to detect failed conditions.
        /// </summary>
        [ServicingStep]
        public void SetupCollectionTestPartition(ServicingContext servicingContext)
        {
            ISqlConnectionInfo connectionInfo = servicingContext.GetConnectionInfo();

            string setupTestPartitionToken;

            bool setupTestPartition = false;

            IVssRequestContext deploymentRequestContext = servicingContext.DeploymentRequestContext;

            if (deploymentRequestContext.ExecutionEnvironment.IsDevFabricDeployment)
            {
                // For bacpac compatibility, do not setup an offline partition in the Export pool.
                TeamFoundationDatabaseManagementService dbms = deploymentRequestContext.GetService<TeamFoundationDatabaseManagementService>();

                List<string> databases = dbms.QueryDatabases(deploymentRequestContext, DatabaseManagementConstants.CollectionExportPool).Select(db => db.DatabaseName).ToList();
                
                string name = connectionInfo.DataSource + ";" + connectionInfo.InitialCatalog;

                if (databases.Contains(name))
                {
                    servicingContext.LogInfo("Not setting up offline test partition for staging and export collection pools in devfabric.");
                    return;
                }
            }

            TeamFoundationRegistryService registryService =
                deploymentRequestContext.GetService<TeamFoundationRegistryService>();

            setupTestPartitionToken = registryService.GetValue(deploymentRequestContext, FrameworkServerConstants.ServicingSetupTestPartition, "False");
            setupTestPartition = string.Equals(setupTestPartitionToken, "True", StringComparison.OrdinalIgnoreCase);

            if (setupTestPartition)
            {
                using (TestPartitionComponent component = connectionInfo.CreateComponentRaw<TestPartitionComponent>(commandTimeout:0))
                {
                    component.SetupTestPartition();
                }
            }
        }

        [ServicingStep]
        public void ReleaseExportTarget(ServicingContext servicingContext, ReleaseExportTargetStepData stepData)
        {
            LogObjectProperties(servicingContext, stepData);

            if (!stepData.DeleteStagingDatabase)
            {
                servicingContext.LogInfo("DeleteStagingDatabase is 'false'. This database will not be released back to the pool. WARNING: This could result in a full export pool.");
                return;
            }

            // Get the properties of the target database
            IVssRequestContext deploymentContext = servicingContext.DeploymentRequestContext;
            TeamFoundationDatabaseManagementService dbms = deploymentContext.GetService<TeamFoundationDatabaseManagementService>();

            dbms.UpdateDatabaseProperties(deploymentContext, stepData.TargetDatabaseId, (dbProperties) =>
            {
                // Set tenant count to 0
                dbProperties.Tenants = 0;
                dbProperties.ServiceLevel = "";
            });

            servicingContext.LogInfo("Released all tenants from database. DatabseId: {0}", stepData.TargetDatabaseId);
        }

        private bool CanConnect(ServicingContext servicingContext, string connectionString)
        {
            servicingContext.LogInfo("Trying to connect to the following database. Connection string: {0}", ConnectionStringUtility.MaskPassword(connectionString));

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    new RetryManager(3, TimeSpan.FromSeconds(30)).Invoke(() => conn.Open());
                }
                return true;
            }
            catch (Exception ex)
            {
                servicingContext.LogInfo(ex.ToString());
                servicingContext.LogInfo("Cannot connect to the specified database. Assuming that database does not exist.");
                return false;
            }
        }

        private bool DropDataTierLogins(ServicingContext servicingContext, List<ISqlConnectionInfo> dataTierConnectionInfos)
        {
            servicingContext.LogInfo("Dropping data tier logins");
            bool result = true;

            foreach (ISqlConnectionInfo dataTierConnectionInfo in dataTierConnectionInfos)
            {
                if (!DropDataTierLogin(servicingContext, dataTierConnectionInfo))
                {
                    result = false;
                }
            }

            return result;
        }

        private bool DropDataTierLogin(ServicingContext servicingContext, ISqlConnectionInfo connectionInfo)
        {
            if (!(connectionInfo is ISupportSqlCredential)) return false;
            String login = ((ISupportSqlCredential)connectionInfo).UserId;

            bool result = true;
            SqlConnectionStringBuilder connectionStringBuilder = new SqlConnectionStringBuilder(connectionInfo.ConnectionString);

            servicingContext.LogInfo("Dropping data tier login {0} on the following SQL Server: {1}",
                login,
                TeamFoundationDataTierService.GetDataSourceWithoutProtocol(connectionInfo.ConnectionString));

            // We are using data tier login to drop user in master database and login itself. 
            // This cannot be done, because as soon as we drop user in master, we will not be able to connect to it.
            // Even if we were able to connect to master, we lost membership in the loginmanager role and cannot drop logins.
            // 
            // We will drop user in master database that are not mapped to logins. After that we drop login.
            // This way we will have at most 1 user in master that is not mapped to a login even if we redeploy 100 times.
            using (TeamFoundationSqlSecurityComponent securityComponent = connectionInfo.CreateComponentRaw<TeamFoundationSqlSecurityComponent>())
            {
                try
                {
                    servicingContext.LogInfo("Querying master database users");
                    List<SqlDatabaseUser> dbUsers = securityComponent.GetDatabaseUsers();

                    foreach (SqlDatabaseUser dbUser in dbUsers)
                    {
                        if (dbUser.HasDbAccess && dbUser.Sid != null && dbUser.Sid.Length > 2 && !dbUser.Name.Equals("dbo", StringComparison.Ordinal))
                        {
                            SqlLoginInfo loginInfo = securityComponent.GetLogin(dbUser.Sid);

                            if (loginInfo == null)
                            {
                                servicingContext.LogInfo("Dropping the following user since it is not mapped to a login: {0}", dbUser.Name);
                                securityComponent.DropDatabaseUser(dbUser.Name);
                                servicingContext.LogInfo("Dropped user successfully.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    servicingContext.TFLogger.Warning(ex);
                }

                try
                {
                    securityComponent.DropLogin(login);
                }
                catch (Exception)
                {
                    // We were trying to drop login that we are currently using to connect to SQL Server.
                    // Sometimes it works and sometimes it fails.
                    // Let us move on.
                }
            }

            return result;
        }

        private bool DropDatabase(ServicingContext servicingContext, TeamFoundationDataTierComponent dataTierComponent, string databaseName, string dataSource)
        {
            bool result = true;

            try
            {
                new RetryManager(3, TimeSpan.FromSeconds(30), ex => servicingContext.TFLogger.Warning(ex)).Invoke(
                    delegate
                    {
                        if (dataTierComponent.CheckIfDatabaseExists(databaseName))
                        {
                            dataTierComponent.DropDatabase(databaseName, DropDatabaseOptions.CloseExistingConnections);
                        }
                    });
            }
            catch (Exception ex)
            {
                servicingContext.TFLogger.Error(ex);
                string message = string.Format(CultureInfo.CurrentCulture,
                    "Failed to drop {0} database on the following SQL Azure Server: {1}. Error: {2}",
                    databaseName,
                    dataSource,
                    ex.Message);

                servicingContext.Error(message);
                result = false;
            }

            return result;
        }

        private void DropDatabases(ServicingContext servicingContext, IEnumerable<ISqlConnectionInfo> connectionInfoList, IEnumerable<string> databaseNames)
        {
            bool result = false;

            foreach (ISqlConnectionInfo connectionInfo in connectionInfoList)
            {
                result = DropDatabases(servicingContext, connectionInfo, databaseNames);
                // Continue to try the next connction string only if last one failed
                if (result)
                {
                    break;
                }
            }

            if (!result)
            {
                servicingContext.Error("Failed to drop databases after trying all the conection strings");
            }
        }

        private bool DropDatabases(ServicingContext servicingContext, ISqlConnectionInfo connectionInfo, IEnumerable<string> databaseNames)
        {
            bool result = true;

            using (TeamFoundationDataTierComponent dataTierComponent = connectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
            {
                foreach (string db in databaseNames)
                {
                    try
                    {
                        new RetryManager(3, TimeSpan.FromSeconds(30), ex => servicingContext.TFLogger.Warning(ex)).Invoke(
                            delegate
                            {

                                if (dataTierComponent.CheckIfDatabaseExists(db))
                                {
                                    dataTierComponent.DropDatabase(db, DropDatabaseOptions.CloseExistingConnections);
                                }
                            });
                    }
                    catch (Exception ex)
                    {
                        servicingContext.TFLogger.Warning(ex);
                        string message = string.Format(CultureInfo.CurrentCulture,
                            "Failed to drop {0} database on the following SQL Azure Server: {1}. Error: {2}",
                            db,
                            connectionInfo.DataSource,
                            ex.Message);

                        servicingContext.Warn(message);
                        result = false;
                    }
                }
            }

            return result;
        }

        private void DropLogins(ServicingContext servicingContext, ISqlConnectionInfo connectionInfo, List<string> logins)
        {
            string dataSource = connectionInfo.DataSource;

            using (TeamFoundationSqlSecurityComponent securityComponent = connectionInfo.CreateComponentRaw<TeamFoundationSqlSecurityComponent>(logger: servicingContext.TFLogger))
            {
                foreach (string login in logins)
                {
                    try
                    {
                        new RetryManager(3, TimeSpan.FromSeconds(30), ex => servicingContext.TFLogger.Warning(ex)).Invoke(
                            delegate
                            {
                                if (securityComponent.GetLogin(login) != null)
                                {
                                    securityComponent.DropLogin(login);
                                }
                            });
                    }
                    catch (Exception ex)
                    {
                        servicingContext.TFLogger.Warning(ex);
                        string message = string.Format(CultureInfo.CurrentCulture,
                            "Failed to drop {0} login on the following SQL Azure Server: {1}. Error: {2}",
                            login,
                            dataSource,
                            ex.Message);

                        // Make it as best effort
                        servicingContext.Warn(message);
                    }
                }
            }
        }

        private Dictionary<string, ISqlConnectionInfo> QueryDataTiers(ServicingContext servicingContext, ISqlConnectionInfo configDbConnectionInfo)
        {
            servicingContext.LogInfo("Querying data tiers");

            Dictionary<string, ISqlConnectionInfo> dataTiers = new Dictionary<string, ISqlConnectionInfo>(VssStringComparer.DataSourceIgnoreProtocol);

            List<ISqlConnectionInfo> connectionInfos;

            using (RegistryComponent registryComponent = configDbConnectionInfo.CreateComponentRaw<RegistryComponent>(60, 200, 20))
            {
                ResultCollection result =
                    registryComponent.QueryRegistry(DatabasePartitionConstants.DeploymentHostId, @"#\Configuration\Hosting\DataTiers\Servers");

                List<RegistryEntry> dataTiersRegistryEntries = result.GetCurrent<RegistryEntry>().ToList();
                connectionInfos = dataTiersRegistryEntries.Where(regEntry => string.Equals(regEntry.Name, "ConnectionString")).Select(regEntry => SqlConnectionInfoFactory.Create(regEntry.Value)).ToList();
            }

            int registryCount = connectionInfos.Count;
            servicingContext.LogInfo("Found {0} data tiers in registry", connectionInfos.Count);

            //now look in the db
            using(DataTierComponent dataTierComponent = configDbConnectionInfo.CreateComponentRaw<DataTierComponent>())
            {
                List<DataTierInfo> dtInfos = dataTierComponent.GetDataTierInfo();

                foreach(var dtInfo in dtInfos)
                {
                    connectionInfos.Add(dtInfo.ConnectionInfo);
                }
            }

            servicingContext.LogInfo("Found {0} data tiers in the DataTier table", connectionInfos.Count - registryCount);

            foreach (ISqlConnectionInfo connectionInfo in connectionInfos)
            {
                string dataSource = TeamFoundationDataTierService.GetDataSourceWithoutProtocol(connectionInfo.ConnectionString);

                servicingContext.LogInfo("DataTier: {0}", ConnectionStringUtility.MaskPassword(connectionInfo.ConnectionString));
                dataTiers.Add(dataSource, connectionInfo);
            }

            return dataTiers;
        }

        /// <summary>
        /// Queries all databases.
        /// </summary>
        /// <param name="configDbConnectionString"></param>
        /// <returns>ILookup, key = SQL Azure Server name, value = list of the TeamFoundationDatabaseProperties objects for the databases on that server.</returns>
        private ILookup<string, InternalDatabaseProperties> QueryDatabases(ServicingContext servicingContext, ISqlConnectionInfo configDbConnectionInfo)
        {
            servicingContext.LogInfo("Querying databases");

            List<InternalDatabaseProperties> databases;

            using (DatabaseManagementComponent component = configDbConnectionInfo.CreateComponentRaw<DatabaseManagementComponent>())
            {
                databases = component.QueryDatabases().GetCurrent<InternalDatabaseProperties>().ToList();
            }

            servicingContext.LogInfo("Found {0} databases", databases.Count);

            // Key - SQL Azure Server name
            // Value - list of the TeamFoundationDatabaseProperties objects
            ILookup<string, InternalDatabaseProperties> result = databases.ToLookup(
                db => TeamFoundationDataTierService.GetDataSourceWithoutProtocol(db.ConnectionInfoWrapper.ConnectionString), // keySelector
                db => db, // element selector
                StringComparer.OrdinalIgnoreCase);

            return result;
        }

        /// <summary>
        /// Queries all database credentials.
        /// </summary>
        /// <param name="configDbConnectionInfo"></param>
        /// <returns>ILookup, key = DatabaseId, value = list of the TeamFoundationDatabaseCredential objects for that database.</returns>
        private ILookup<int, TeamFoundationDatabaseCredential> QueryDatabaseCredentials(ServicingContext servicingContext, ISqlConnectionInfo configDbConnectionInfo)
        {
            servicingContext.LogInfo("Querying database credentials");

            List<TeamFoundationDatabaseCredential> credentials;

            DatabaseCredentialsComponent component;
            if (TeamFoundationResourceManagementService.TryCreateComponentRaw<DatabaseCredentialsComponent>(configDbConnectionInfo, 60, 200, 25, out component))
            {
                using (component)
                {
                    credentials = component.QueryDatabaseCredentials();
                }

                servicingContext.LogInfo("Found {0} database credentials", credentials.Count);

                // Key - DatabaseId
                // Value - list of the TeamFoundationDatabaseCredential objects
                ILookup<int, TeamFoundationDatabaseCredential> result = credentials.ToLookup(
                    cred => cred.DatabaseId, // keySelector
                    cred => cred); // element selector

                return result;
            }
            else
            {
                servicingContext.Warn("DatabaseCredentials service not registered. This is expected only if the database is pre-Dev12.M60.");

                return null;
            }
        }

        private static string GenerateNameForFailedConfigDatabase(ServicingContext servicingContext, ISqlConnectionInfo connectionInfo, String databaseName)
        {
            string newDatabaseName = string.Format(CultureInfo.InvariantCulture, c_failedDatabaseFormat, databaseName, string.Empty);

            if (newDatabaseName.Length <= c_maxDatabaseNameLen)
            {
                if (CheckIfDatabaseExists(servicingContext, connectionInfo, newDatabaseName))
                {
                    servicingContext.LogInfo(c_databaseExistsFormat, newDatabaseName);

                    for (int i = 1; i < 10; ++i)
                    {
                        newDatabaseName = string.Format(CultureInfo.InvariantCulture, c_failedDatabaseFormat, databaseName, i);

                        if (CheckIfDatabaseExists(servicingContext, connectionInfo, newDatabaseName))
                        {
                            servicingContext.LogInfo(c_databaseExistsFormat, newDatabaseName);
                            newDatabaseName = null;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                newDatabaseName = null;
            }

            if (newDatabaseName == null)
            {
                newDatabaseName = string.Format(CultureInfo.InvariantCulture, c_failedDatabaseFormat, "Tfs_Configuration", Guid.NewGuid());
            }

            return newDatabaseName;
        }        

        private static bool CheckIfDatabaseExists(ServicingContext servicingContext, ISqlConnectionInfo connectionInfo, string databaseName)
        {
            // ensure we're using the master database otherwise the connection may fail
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionInfo.ConnectionString);
            
            if(!String.Equals(builder.InitialCatalog, "master", StringComparison.Ordinal))
            {
                builder.InitialCatalog = "master";
                connectionInfo = connectionInfo.Clone();
                connectionInfo.ConnectionString = builder.ConnectionString;
            }

            using (TeamFoundationDataTierComponent dataTierComponent = connectionInfo.CreateComponentRaw<TeamFoundationDataTierComponent>(logger: servicingContext.TFLogger))
            {
                return dataTierComponent.CheckIfDatabaseExists(databaseName);
            }
        }

        /// <summary>
        /// Sleep for five seconds before retrying to connect to the SQL Server after we failed to connect in SetRcsi method. 
        /// </summary>
        private static readonly TimeSpan s_databaseConnectionFailureRetryPause = TimeSpan.FromSeconds(5);

        /// <summary>
        /// In the SetRcsi method we will keep trying to connect to the database for up to one minute.
        /// </summary>
        private static readonly TimeSpan s_databaseConnectionFailureMaxWait = TimeSpan.FromMinutes(3);

        private const int c_maxDatabaseNameLen = 256; // database name can be up to 256 characters in SQL Server
        private const string c_failedDatabaseFormat = "{0}(failed{1})";
        private const string c_databaseExistsFormat = "Database '{0}' already exists.";
    }

    public class AddSqlRoleMemberStepData
    {
        // optional
        [XmlAttribute("database")]
        public string Database { get; set; }

        [XmlAttribute("account")]
        public string Account { get; set; }

        [XmlAttribute("defaultSchema")]
        public string DefaultSchema { get; set; }

        [XmlAttribute("role")]
        public string Role { get; set; }

        [XmlAttribute("attemptDboChange")]
        public bool AttemptDatabaseOwnerChange { get; set; }
    }

    public class CreateDatabaseStepData
    {
        [XmlAttribute("maxSizeInGB")]
        public int MaxSizeInGB { get; set; }
    }

    [DebuggerDisplay("DatabaseName: {DatabaseName}; SqlServerInstance: {SqlServerInstance}")]
    public class CreateCollectionDatabaseStepData
    {
        [XmlAttribute("databaseName")]
        public string DatabaseName { get; set; }

        [XmlAttribute("sqlServerInstance")]
        public string SqlServerInstance { get; set; }
    }

    public class DeleteDatabaseAttributeStepData
    {
        [XmlAttribute("attribute")]
        public String DatabaseAttribute { get; set; }
    }

    public class DropDatabaseStepData
    {
        [XmlAttribute("dataTierConnectionString")]
        public string DataTierConnectionString { get; set; }

        [XmlAttribute("databaseName")]
        public string DatabaseName { get; set; }

        [XmlAttribute("drop")]
        public bool Drop { get; set; }
    }

    public class DropDeploymentDatabasesStepData
    {
        [XmlAttribute("configDbConnectionString")]
        public string ConfigDbConnectionString { get; set; }

        [XmlAttribute("dataTierConnectionString")]
        public string DataTierConnectionString { get; set; }

        [XmlAttribute("opsConnectionString")]
        public string OpsConnectionString { get; set; }

        [XmlAttribute("drop")]
        public bool Drop { get; set; }
    }

    public class DropUserDatabasesStepData
    {
        [XmlAttribute("opsConnectionString")]
        public string OpsConnectionString { get; set; }
    }

    [DebuggerDisplay("Category: {DatabaseCategory}, Enable: {Enable}")]
    public class SetRcsiStepData
    {
        // True to turn ON rcsi, false to turn OFF rcsi.
        [XmlAttribute("enable")]
        public Boolean Enable { get; set; }
    }

    public class AcquireDatabaseStepData
    {
        [XmlAttribute("targetDatabasePoolName")]
        public string TargetDatabasePoolName { get; set; }
    }

    public class ReleaseExportTargetStepData
    {
        [XmlAttribute("deleteStagingDatabase")]
        public Boolean DeleteStagingDatabase { get; set; }

        [XmlAttribute("targetDatabaseId")]
        public int TargetDatabaseId { get; set; }
    }
}
