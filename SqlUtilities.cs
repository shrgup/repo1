using System;
using System.Management;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using MS.TF.Test.Deploy.Components;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Principal;
using System.IO;
using Microsoft.Win32;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace MS.TF.Test.Deploy
{
    public enum DatabaseCategory
    {
        Framework,
        Analysis,
        Warehouse,
        Build,
        DeploymentRig,
        Integration,
        LabExecution,
        TestManagement,
        TestRig,
        VersionControl,
        WorkItem,
        WorkItemAttachment,
    }

    public enum LocationMappingServiceType
    {
        ReportManagerUrl,
        ReportWebServiceUrl,
        WssAdminUrl,
        WssRootUrl,
    }

    public static class SqlUtilities
    {
        // restoring large (50+ Gigs) DBs can take over an hour, so set the timeout high
        public static int TimeoutMinutes = 120;
        public static string TfsAdminRoleName = "TFSADMINROLE";
        public static string TfsExecRoleName = "TFSEXECROLE";
        public static string[] WSS3Databases = { "WSS_AdminContent", "WSS_Config", "WSS_Content" };
        public static string TfsWarehouseAdministrator = "TfsWarehouseAdministrator";
        public static string TfsWarehouseDataReader = "TfsWarehouseDataReader";
        public static string RegistryItemParentPathString = @"#\Configuration\Database\{0}\";
        static string RegistryItemChildItemString = @"ConnectionString\";
        static string Tfs = "Tfs_";
        static string Configuration = "Configuration";

        /// <summary>
        /// Returns a SQL Connection string
        /// </summary>
        /// <param name="db"></param>
        /// <param name="databaseName"></param>
        /// <returns></returns>
        public static string GetConnectionString(SqlDatabaseComponent db, string databaseName)
        {
            return GetConnectionString(db.Identifier, databaseName, TimeoutMinutes * 60);
        }

        /// <summary>
        /// Returns the connection string for the machine and database named
        /// </summary>
        /// <param name="identifier">example: machineName\instanceName</param>
        /// <param name="database">example: master</param>
        /// <returns></returns>
        public static string GetConnectionString(string identifier, string databaseName)
        {
            return GetConnectionString(identifier, databaseName, TimeoutMinutes * 60);
        }

        /// <summary>
        /// Returns a SQL Connection string
        /// </summary>
        /// <param name="db"></param>
        /// <param name="databaseName"></param>
        /// <returns></returns>
        public static string GetConnectionString(string dbIdentifier, string databaseName, int timeout)
        {
            // scrub FQDN names from the identifier since SQL does not like them.
            string[] parts = dbIdentifier.Split('\\');
            string machineName = dbIdentifier;
            string instanceName = String.Empty;
            if (parts.Length > 1)
            {
                machineName = parts[0];
                instanceName = parts[1];
            }

            parts = machineName.Split('.');
            if (parts.Length > 1)
            {
                machineName = parts[0];
            }

            if (!String.IsNullOrEmpty(instanceName))
            {
                dbIdentifier = String.Format(@"{0}\{1}", machineName, instanceName);
            }
            else
            {
                dbIdentifier = machineName;
            }

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = dbIdentifier;
            builder.IntegratedSecurity = true;
            builder.ConnectTimeout = timeout;

            if (!string.IsNullOrEmpty(databaseName))
            {
                builder.InitialCatalog = databaseName;
            }

            return builder.ConnectionString;
        }

        /// <summary>
        /// Tests the SQL connection and returns true if it succeeds
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <returns></returns>
        public static bool DoesInstanceExist(string dbIdentifier)
        {
            string connectionString = GetConnectionString(dbIdentifier, "master", 5);
            TraceFormat.WriteLine("Testing {0}", connectionString);
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                }
                return true;
            }
            catch
            {
                TraceFormat.WriteWarning("Failed to connect to SQL Instance '{0}'", dbIdentifier);
                return false;
            }
        }

        public static bool DoesTableExist(string dbConnectionString, string tableName)
        {
            return GetDatabaseTables(dbConnectionString).Contains(tableName);
        }

        /// <summary>
        /// Returns the number of rows on the table
        /// </summary>
        /// <param name="dbConnectionString">database connection string</param>
        /// <param name="tableName">table name</param>
        /// <returns>the number of rows on the table or 0 if the count can't be determined, i.e. the table does not exist</returns>
        public static int CountRowsOnTable(string dbConnectionString, string tableName)
        {
            string sql = "select count(*) from [" + tableName + "]";
            int count = 0;
            using (SqlConnection conn = new SqlConnection(dbConnectionString))
            {
                SqlCommand cmd = new SqlCommand(sql, conn);

                try
                {
                    conn.Open();
                    count = (Int32)cmd.ExecuteScalar();
                }
                catch (Exception ex)
                {
                    Trace.TraceInformation("Exception counting the rows on table {0}, Exception: {1}", tableName, ex.ToString());
                }
                return count;
            }
        }
        /// <summary>
        /// Execute a SQL command on the specified database
        /// </summary>
        /// <param name="dbIdentifier"> machine\instance or just machine</param>
        /// <param name="dbName">name of database</param>
        /// <param name="statement">SQL statement to execute</param>
        /// <returns></returns>
        public static bool SqlExecuteNonQuery(string dbIdentifier, string dbName, string statement, bool rethrow = false)
        {
            return SqlExecuteNonQuery(dbIdentifier, dbName, statement, TimeoutMinutes * 60, rethrow);
        }

        /// <summary>
        /// Execute a SQL command on the specified database
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <param name="dbName"></param>
        /// <param name="statement"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static bool SqlExecuteNonQuery(string dbIdentifier, string dbName, string statement, int timeout, bool rethrow = false)
        {
            return SqlExecuteNonQuery(dbIdentifier, dbName, statement, timeout, null, rethrow);
        }

        /// <summary>
        /// Execute a SQL command on the specified database
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <param name="dbName"></param>
        /// <param name="statement"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static bool SqlExecuteNonQuery(string dbIdentifier, string dbName, string statement, int timeout, SqlParameter[] parameters, bool rethrow = false)
        {
            string connectionString = GetConnectionString(dbIdentifier, dbName, timeout);

            if (!DoesInstanceExist(dbIdentifier))
            {
                return false;
            }

            TraceFormat.WriteLine("SqlExecuteNonQuery '{0}'", connectionString);
            TraceFormat.WriteLine("Statement: {0}", statement);
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    SqlCommand command = new SqlCommand(statement)
                    {
                        Connection = connection,
                        CommandTimeout = timeout,
                    };

                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }

                    command.ExecuteNonQuery();
                }
                return true;
            }
            catch (SqlException e)
            {
                TraceFormat.WriteError("SqlExecuteNonQuery threw SQL Exception {0}", e.Message);
                if (rethrow)
                {
                    throw;
                }
                return false;
            }
        }

        /// <summary>
        /// Execute a script of multiple non-queries
        /// Split the script into individual commands and run them in order
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <param name="dbName"></param>
        /// <param name="script"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static bool SqlExecuteNonQueryScript(string dbIdentifier, string dbName, string script, int timeout)
        {
            string[] scriptLines = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline);

            string connectionString = GetConnectionString(dbIdentifier, dbName, timeout);

            TraceFormat.WriteLine("SqlExecuteNonQueryScript '{0}'", connectionString);

            if (!DoesInstanceExist(dbIdentifier))
            {
                return false;
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    foreach (string statement in scriptLines)
                    {
                        if (!String.IsNullOrEmpty(statement))
                        {
                            SqlCommand command = new SqlCommand(statement)
                            {
                                Connection = connection,
                                CommandTimeout = timeout,
                            };
                            try
                            {
                                command.ExecuteNonQuery();
                            }
                            catch (SqlException e)
                            {
                                TraceFormat.WriteError("SqlExecuteNonQueryScript threw Sql Exception on statement '{0}' Exception:{1}", statement, e.Message);
                                return false;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                TraceFormat.WriteError("SqlExecuteNonQueryScript threw Exception: {0}", e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Execute a SQL query that returns a scalar
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <param name="dbName"></param>
        /// <param name="statement"></param>
        /// <returns></returns>
        public static object SqlExecuteScalar(string dbIdentifier, string dbName, string statement)
        {
            return SqlExecuteScalar(dbIdentifier, dbName, statement, TimeoutMinutes * 60);
        }

        /// <summary>
        /// Execute a SQL query that returns a scalar
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <param name="dbName"></param>
        /// <param name="statement"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public static object SqlExecuteScalar(string dbIdentifier, string dbName, string statement, int timeout)
        {
            object obj = null;

            string connectionString = GetConnectionString(dbIdentifier, dbName, timeout);

            Trace.WriteLine("SqlExecuteScalar");
            TraceFormat.WriteLine("ConnectionString: {0}", connectionString);
            TraceFormat.WriteLine("Statement: {0}", statement);

            if (!DoesInstanceExist(dbIdentifier))
            {
                return false;
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    SqlCommand command = new SqlCommand(statement)
                    {
                        Connection = connection,
                        CommandTimeout = timeout,
                    };

                    obj = command.ExecuteScalar();
                }
                return obj;
            }
            catch (SqlException e)
            {
                TraceFormat.WriteError("SqlExecuteScalar threw SQL Exception {0}", e.Message);
                return null;
            }
        }

        public class RestoreInfo
        {
            public string DatabaseName;
            public string Script;
        }

        private class RestoreDetails
        {
            public string DatabaseName;
            public string FileName;
            public string Script;
            //public string LogicalName;
            public List<string> DiskFileLines;
            public List<string> MoveFileLines;
        }

        public static List<RestoreInfo> CreateRestoreScripts(string dbIdentifier, string bakPath, List<String> backupList, string restorePath, string restorePrefix)
        {
            List<RestoreDetails> detailList = new List<RestoreDetails>();
            List<RestoreInfo> restoreList = new List<RestoreInfo>();

            if (!DoesInstanceExist(dbIdentifier))
            {
                return restoreList;
            }

            string connectionString = GetConnectionString(dbIdentifier, "master", 300);

            // this prefix will only show up on the files on disk, ie the .ldf and .mdf files
            // and is intended to avoid name conflicts which you sometimes get with multiple SharePoint content DBs
            // or multiple imported collections, ie, TfsVersionControl.mdf
            string randomPrefix = SeededRandom.Random.Next(100000).ToString();

            foreach (string bakName in backupList)
            {

                try
                {
                    using (SqlConnection connection = new SqlConnection(connectionString))
                    {
                        connection.Open();
                        string statement = String.Format(@"RESTORE FILELISTONLY FROM DISK = '{0}\{1}.bak'", bakPath, bakName);

                        SqlCommand command = new SqlCommand(statement)
                        {
                            Connection = connection,
                            CommandTimeout = 300,
                        };

                        string mdfFileName = null;
                        //string logicalDbName = null;
                        List<string> moveLines = new List<string>();

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string logicalName = reader.GetString(0);
                                string physicalName = reader.GetString(1);
                                // swap the default physical name path with the desired restore path and prefixed filename
                                string restoreFile = String.Format(@"{0}\{1}_{2}", restorePath, randomPrefix, Path.GetFileName(physicalName));
                                string moveLine = String.Format(" MOVE N'{0}' to N'{1}',", logicalName, restoreFile);
                                moveLines.Add(moveLine);

                                if (restoreFile.EndsWith("mdf", StringComparison.OrdinalIgnoreCase))
                                {
                                    // record one of the file names to return back
                                    mdfFileName = restoreFile;

                                    // record the database name too
                                    //logicalDbName = logicalName;
                                }
                            }
                        }

                        string restoreName = SqlUtilities.PrefixedDatabaseName(restorePrefix, bakName);

                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine(String.Format(@"RESTORE DATABASE [{0}] FROM", restoreName));
                        string diskLine = String.Format(@"DISK = N'{0}\{1}.bak'", bakPath, bakName);
                        sb.AppendFormat(@"{0} WITH FILE = 1,", diskLine);
                        foreach (string moveLine in moveLines)
                        {
                            sb.AppendLine(moveLine);
                        }
                        sb.AppendFormat(" NOUNLOAD,  STATS = 10");

                        detailList.Add(new RestoreDetails()
                        {
                            DatabaseName = restoreName,
                            FileName = mdfFileName,
                            //LogicalName = logicalDbName,
                            DiskFileLines = new List<String>() { diskLine },
                            Script = sb.ToString(),
                            MoveFileLines = moveLines,
                        });
                    }
                }
                catch (SqlException e)
                {
                    TraceFormat.WriteError("SqlExecuteReader threw SQL Exception {0}", e.Message);
                }
            }

            List<RestoreDetails> tempList = new List<RestoreDetails>();

            // check for and consolidate files that are actually a single database
            foreach (RestoreDetails info in detailList)
            {
                bool found = false;
                foreach (RestoreDetails consolidated in tempList)
                {
                    if (String.Equals(info.FileName, consolidated.FileName))
                    {
                        // we need to consolidate these files into a single database

                        // find the common name, ie Tfs_Foo_1 and Tfs_Foo_2 -> Tfs_Foo
                        for (int index = consolidated.DatabaseName.Length; index > 0; index--)
                        {
                            string candidateName = consolidated.DatabaseName.Substring(0, index).TrimEnd('_');
                            if (info.DatabaseName.StartsWith(candidateName))
                            {
                                TraceFormat.WriteLine("consolidating {0} + {1} -> {2}", consolidated.DatabaseName, info.DatabaseName, candidateName);
                                consolidated.DatabaseName = candidateName;
                                break;
                            }
                        }

                        consolidated.DiskFileLines.Add(info.DiskFileLines[0]);

                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    tempList.Add(info);
                }
            }

            detailList = tempList;

            // I cannot use a foreach here since I am updating the Script property
            for (int index = 0; index < detailList.Count; index++)
            {
                RestoreDetails info = detailList[index];
                if (info.DiskFileLines.Count > 1)
                {
                    // we need to rebuild the restore script
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(String.Format(@"RESTORE DATABASE [{0}] FROM", info.DatabaseName));
                    for (int fileCount = 0; fileCount < info.DiskFileLines.Count - 1; fileCount++)
                    {
                        sb.AppendLine(String.Format(@"{0},", info.DiskFileLines[fileCount]));
                    }
                    sb.AppendLine(String.Format(@"{0} WITH FILE = 1,", info.DiskFileLines[info.DiskFileLines.Count - 1]));

                    foreach (string file in info.MoveFileLines)
                    {
                        sb.AppendLine(String.Format(@"{0}", file));
                    }
                    sb.AppendFormat(" NOUNLOAD,  STATS = 10");
                    info.Script = sb.ToString();
                }
            }

            foreach (RestoreDetails details in detailList)
            {
                restoreList.Add(new RestoreInfo() { DatabaseName = details.DatabaseName, Script = details.Script });
            }

            return restoreList;
        }

        /// <summary>
        /// Deletes the specified database
        /// </summary>
        /// <param name="server"></param>
        /// <param name="dbname"></param>
        public static bool DropDatabase(string dbIdentifier, string dbname)
        {
            if (string.IsNullOrEmpty(dbIdentifier))
            {
                throw new ArgumentNullException("dbIdentifier");
            }
            if (string.IsNullOrEmpty(dbname))
            {
                throw new ArgumentNullException("dbname");
            }

            if (!DoesInstanceExist(dbIdentifier))
            {
                return false;
            }

            string killStatement = Utilities.GetResourceAsString("stmt_KillConnections.sql", Assembly.GetExecutingAssembly());

            string dropStatement1 = String.Format(@"
ALTER DATABASE [{0}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE
DROP DATABASE [{0}]
", dbname);

            string dropStatement2 = Utilities.GetResourceAsString("stmt_DropDatabase.sql", Assembly.GetExecutingAssembly());

            int retriesRemaining = 5;
            do
            {
                if (!DatabaseExists(dbIdentifier, dbname))
                {
                    return true;
                }

                try
                {
                    // first try the simple statements
                    TraceFormat.WriteLine("Killing connections to DB");
                    SqlParameter dbNameParameter = new SqlParameter("@databaseName", SqlDbType.NVarChar, 128);
                    dbNameParameter.Value = dbname;
                    SqlParameter[] parameters = new SqlParameter[] { dbNameParameter };

                    SqlExecuteNonQuery(dbIdentifier, "master", killStatement, 5 * 60, parameters, true);
                    TraceFormat.WriteLine("dropping DB");
                    SqlExecuteNonQuery(dbIdentifier, "master", dropStatement1, 5 * 60, true);
                    return true;
                }
                catch
                {
                }

                try
                {
                    //also switching to use the rethrow flag so we can actually try again since SqlExecuteNonQuery was swallowing exceptions...
                    //not sure this is a good idea for this command since it seems that the command did continue to drop the database
                    //even though the connection timed out...what happens here if we try again and there is a drop in progress?
                    //or...the database was already dropped by the time we got back in here?
                    SqlParameter dbNameParameter = new SqlParameter("@databaseName", SqlDbType.NVarChar, 128);
                    dbNameParameter.Value = dbname;

                    SqlParameter[] parameters = new SqlParameter[] { dbNameParameter };

                    SqlExecuteNonQuery(dbIdentifier, "master", dropStatement2, 5 * 60, parameters, true);
                    return true;
                }
                catch (Exception ex)
                {
                    TraceFormat.WriteError("Caught exception attempting to drop database '{0};{1}': {2}", dbIdentifier, dbname, ex.Message);
                    retriesRemaining--;
                    if (retriesRemaining > 0)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(10));
                    }
                }
            } while (retriesRemaining > 0);

            return false;
        }

        /// <summary>
        /// Returns TRUE if the database exists
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// <returns></returns>
        public static bool DatabaseExists(string sqlInstance, string dbName)
        {
            if (string.IsNullOrEmpty(dbName))
            {
                return false;
            }

            string query = String.Format("SELECT count(*) from dbo.sysdatabases WHERE name='{0}'", dbName);

            int count = 0;

            try
            {
                count = (int)SqlExecuteScalar(sqlInstance, "master", query, 5);
            }
            catch (Exception e)
            {
                TraceFormat.WriteLine("DatabaseExists() hit an exception: {0}", e.Message);
            }

            bool exists = count > 0;
            TraceFormat.WriteLine("Database '{0}' exists on '{1}' ? {2}", dbName, sqlInstance, exists);

            return exists;
        }

        /// <summary>
        /// Backup the database named 'dbPrefix'+'dbName' to a backup file named 'dbName'.bak
        /// Example: dbPrefix = tk18, dbFileName = TfsIntegration 
        /// results in database 'tk18TfsIntegration' being backed up to file name 'TfsIntegration.bak'
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <param name="dbPrefix"></param>
        /// <param name="dbFileName"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static bool BackupDatabase(string dbIdentifier, string dbName, string filePath)
        {
            if (string.IsNullOrEmpty(dbIdentifier))
            {
                throw new ArgumentNullException("dbIdentifier");
            }
            if (string.IsNullOrEmpty(dbName))
            {
                throw new ArgumentNullException("dbName");
            }

            DisableOfflinePartition(dbIdentifier, dbName);

            string statement = string.Format(@"BACKUP DATABASE [{0}] TO DISK = N'{1}\{0}.bak' WITH COMPRESSION, NOFORMAT, NOINIT,  NAME = N'{0}-Full Database Backup', SKIP, NOREWIND, NOUNLOAD,  STATS = 10", dbName, filePath);
            bool success = false;

            try
            {
                TraceFormat.WriteLine("Attempting to backup {0} on {1} to {2}\\{3}.bak", dbName, dbIdentifier, filePath, dbName);
                success = SqlExecuteNonQuery(dbIdentifier, "master", statement, rethrow: true);
            }
            catch (Exception e)
            {
                if (!String.IsNullOrEmpty(e.Message) &&
                    e.Message.Contains("WITH COMPRESSION"))
                {
                    TraceFormat.WriteLine("Backup with compression failed.  Trying again without compression");
                    statement = statement.Replace("COMPRESSION,", String.Empty);
                    try
                    {
                        success = SqlExecuteNonQuery(dbIdentifier, "master", statement, rethrow: false);
                    }
                    catch (Exception e2)
                    {
                        TraceFormat.WriteError("Caught Exception = {0}", e2.Message);
                    }
                }
                else
                {
                    TraceFormat.WriteError("Caught Exception = {0}", e.Message);
                }
            }

            if (!success)
            {
                TraceFormat.WriteError("failed to backup {0} on {1} to {2}\\{3}.bak", dbName, dbIdentifier, filePath, dbName);
            }

            return success;
        }

        public static bool DisableOfflinePartition(string dbIdentifier, string dbName)
        {
            try
            {
                // Step 1:  Get the backup file that gets created when we enable the test partition
                TraceFormat.WriteLine("Checking for offline partition");
                string statement = @"SELECT  STUFF(physical_name, LEN(physical_name) - 2, 3, 'bak')
                                    FROM    sys.database_files 
                                    WHERE   name = 'LeadingKey'
                                    AND state_desc = 'OFFLINE'";

                object result = SqlExecuteScalar(dbIdentifier, dbName, statement);
                if (result == null)
                {
                    // this database does not have an offline partition
                    return true;
                }

                // Step 2: Restore the backup file

                TraceFormat.WriteLine("Database {0} has an offline partition.  Removing it.", dbName);
                string filename = (String)result;

                statement = String.Format(@"RESTORE DATABASE [{0}]
                                            FILE = 'LeadingKey'
                                            FROM  DISK = '{1}'",
                                            dbName, filename);
                SqlExecuteNonQuery(dbIdentifier, "master", statement);

                // Step 3: Disable the test partition
                SqlExecuteNonQuery(dbIdentifier, dbName, "EXEC prc_DisableTestPartition");
            }
            catch (Exception e)
            {
                TraceFormat.WriteError("Failed to disable offline partition.\n Exception: {0}", e.Message);
                return false;
            }

            TraceFormat.WriteLine("Successfully disabled offline partition");
            return true;
        }

        /// <summary>
        /// Retrieves a list of databases from sysdatabases table matching the specified pattern
        /// pattern can contain wildcards using the '*' or '%' character
        /// </summary>
        /// <param name="dbIdentifier"></param>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public static List<string> RetrieveDatabases(string dbIdentifier, string pattern)
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                pattern = pattern.Replace('*', '%');
            }
            if (string.IsNullOrEmpty(dbIdentifier))
            {
                throw new ArgumentNullException("dbIdentifier");
            }

            List<string> tfsDBs = new List<string>();
            string statement = string.Format(@"select name from sysdatabases where name like '{0}'", pattern);

            if (!DoesInstanceExist(dbIdentifier))
            {
                return tfsDBs;
            }

            // 2 minutes is plenty to list databases
            int timeout = 2 * 60;

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = dbIdentifier;
            builder.IntegratedSecurity = true;
            builder.ConnectTimeout = timeout;

            TraceFormat.WriteLine("Statement: {0}", statement);
            try
            {
                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    connection.Open();

                    SqlCommand command = new SqlCommand()
                    {
                        Connection = connection,
                        CommandTimeout = timeout,
                    };
                    statement = statement.Replace("'", "''");
                    command.CommandText = "exec('" + statement + "')";

                    DataSet dataset = new DataSet();
                    SqlDataAdapter dataAdapter = new SqlDataAdapter();
                    dataAdapter.SelectCommand = command;
                    dataAdapter.Fill(dataset);
                    command.Dispose();

                    foreach (DataRow row in dataset.Tables[0].Rows)
                    {
                        tfsDBs.Add(row["name"].ToString());
                    }
                }
            }
            catch (Exception exc)
            {
                TraceFormat.WriteError("Cannot retrieve databases.. {0}", exc.Message);
            }
            return tfsDBs;
        }


        /// <summary>
        /// Extract the machine name from the instance string
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        public static string GetHostNameFromInstance(string instance)
        {
            if (string.IsNullOrEmpty(instance))
            {
                throw new ArgumentNullException("instance");
            }

            string[] results = instance.Split(new char[] { '\\', ':', ',' }, 2);
            return results[0];
        }

        /// <summary>
        /// get the member role as a list
        /// <param name="roleName">the role to retrieve</param>
        /// <param name="connectionString">the connection string to the database</param>
        /// </summary>
        /// <returns>the list of role members</returns>
        public static List<string> GetMembersFromRole(string roleName, string sqlInstance, string dbName)
        {
            List<string> memberList = new List<string>();
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, dbName)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(string.Format("EXEC sp_helprolemember '{0}'", roleName), conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                memberList.Add(reader.GetString(1));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to Get Members From Role.\n Exception: {0}", ex);
            }
            return memberList;
        }

        /// <summary>
        /// Check if the specified role exists in the specified db
        /// <param name="roleName"></param>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// </summary>
        /// <returns>bool true is returned if the role exists</returns>
        public static bool IsRoleNameInDb(string roleName, string sqlInstance, string dbName)
        {
            bool success = false;
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, dbName)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(string.Format("EXEC sp_helprole '{0}'", roleName), conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            if (reader.HasRows)
                            {
                                success = true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to get role {0} from sqlinstance {1} and db {2} .\n Exception: {3}", roleName, sqlInstance, dbName, ex);
            }
            return success;
        }

        /// <summary>
        /// check if a member is in a role
        /// <param name="roleName">the role to retrieve</param>
        /// <param name="memberToCheck">the member name to check in the role</param>
        /// <param name="sqlInstance">the sql Instance where the DB is stored</param>
        /// <param name="dbName">the database</param>
        /// </summary>
        /// <returns>bool true is returned if the member is in the role or false if not</returns>
        public static bool IsMemberInRole(string roleName, string memberToCheck, string sqlInstance, string dbName)
        {
            TraceFormat.WriteLine("Verifying if {0} is in the SQL {1} role, using instance {2} and db {3}",
                memberToCheck, roleName, sqlInstance, dbName);
            List<string> memberList = GetMembersFromRole(roleName, sqlInstance, dbName);
            foreach (string member in memberList)
            {
                TraceFormat.WriteLine(" Trying '{0}'", member);
                if (AccountHelper.AreAccountsEqual(member, memberToCheck))
                {
                    TraceFormat.WriteLine(" Found, it is a member");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes the given member from the given role
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="member"></param>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        public static void RemoveMemberFromRole(string roleName, string member, string sqlInstance, string dbName)
        {
            TraceFormat.WriteLine("Removing member {0} from SQL role {1}, using instance {2} and db {3}",
                member, roleName, sqlInstance, dbName);
            SqlExecuteNonQuery(sqlInstance, dbName, String.Format("EXEC sp_droprolemember '{0}', '{1}'", roleName, member), TimeoutMinutes * 1, rethrow: true);
        }

        public static void AddMemberToRole(string roleName, string member, string sqlInstance, string dbName)
        {
            TraceFormat.WriteLine("Adding member {0} to SQL role {1}, using instance {2} and db {3}",
                member, roleName, sqlInstance, dbName);
            SqlExecuteNonQuery(sqlInstance, dbName, String.Format("EXEC sp_addrolemember '{0}', '{1}'", roleName, member), TimeoutMinutes * 1, rethrow: true);
        }

        /// <summary>
        /// Validates if the user is the Database Owner
        /// </summary>
        /// <param name="user">the user should be in the form domain\user</param>
        /// <param name="instance">The SQL instance</param>
        /// <param name="db">the Database name</param>
        /// <returns></returns>
        public static bool IsDatabaseOwner(string user, string sqlInstance, string dbName)
        {
            TraceFormat.WriteLine("Validating if {0} is the owner of db {1} in instance {2}", user, dbName, sqlInstance);
            string owner = string.Empty;
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, dbName)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(@"SELECT SUSER_SNAME(sid) FROM sysusers WHERE name = 'dbo'", conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            owner = reader.Read() ? reader.GetString(0) : string.Empty;
                            TraceFormat.WriteLine("Found '{0}'", owner);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to Get The DB Owner.\n Exception: {0}", ex);
            }
            return AccountHelper.AreAccountsEqual(user, owner);
        }

        /// <summary>
        /// Computes the Warehouse db name using the prefix
        /// </summary>
        /// <param name="prefix">db prefix</param>
        /// <returns></returns>
        public static string TfsWarehouseDBName(string prefix)
        {
            return string.Format("Tfs_{0}Warehouse", prefix);
        }

        /// <summary>
        /// Computes the anlaysis db name using the prefix
        /// </summary>
        /// <param name="prefix">db prefix</param>
        /// <returns></returns>
        public static string TfsAnalysisDBName(string prefix)
        {
            return string.Format("Tfs_{0}Analysis", prefix);
        }

        /// <summary>
        /// Computes the Configuration db name using the prefix
        /// </summary>
        /// <param name="prefix">db prefix</param>
        /// <returns></returns>
        public static string TfsConfigurationDBName(string prefix)
        {
            return string.Format("Tfs_{0}Configuration", prefix);
        }

        /// <summary>
        /// Computes the ParentPath column in the tbl_RegistryItems
        /// </summary>
        /// <param name="dbCategory"></param>
        /// <returns></returns>
        public static string RegistryItemParentPath(DatabaseCategory dbCategory)
        {
            return string.Format(RegistryItemParentPathString, dbCategory);
        }

        /// <summary>
        /// Returns the Database Name using the collection name
        /// </summary>
        /// <param name="collectionName">Collection Database</param>
        /// <returns></returns>
        public static string TfsCollectionDBName(string collectionName)
        {
            return "Tfs_" + collectionName;
        }

        /// <summary>
        /// Gets and returns the AT Service accounts 
        /// </summary>
        /// <param name="sqlInstance">sql Instance where the configuration db is stored</param>
        /// <param name="database">db prefix</param>
        /// <returns></returns>
        public static List<String> GetATServiceAccounts(string sqlInstance, string database)
        {
            TraceFormat.WriteLine("Getting the AT service Accounts from sqlIsntance {0}, db {1}", sqlInstance, database);
            //string serviceGroupName = AssemblyHelper.ExtractStringFormResource(new ResourceDefinition("Microsoft.TeamFoundation.Admin.Deploy.Application.dll", "ResourceStrings", "ServiceGroupName"));
            //string queryString = string.Format(
            string queryString =
@"select domain, account_name from tbl_security_identity_cache, tbl_gss_groups, tbl_gss_group_membership
where tbl_gss_groups.display_name = 'Team Foundation Service Accounts'
and tbl_gss_group_membership.parent_group_id = tbl_gss_groups.tf_id
and tbl_security_identity_cache.tf_id = tbl_gss_group_membership.member_id
";
            //", serviceGroupName);

            List<String> accounts = new List<String>();
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, database)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(queryString, conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string account = reader.GetString(0) + "\\" + reader.GetString(1);
                                TraceFormat.WriteLine(" Member Found '{0}'", account);
                                accounts.Add(account);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to Get The Accounts.\n Exception: {0}", ex);
            }
            return accounts;
        }

        /// <summary>
        /// Gets and returns the Wss Service accounts 
        /// </summary>
        /// <param name="sqlInstance">sql Instance where the configuration db is stored</param>
        /// <param name="database">db prefix</param>
        /// <returns></returns>
        public static List<String> GetWssServiceAccounts(string sqlInstance, string database)
        {
            TraceFormat.WriteLine("Getting the Wss service Accounts from sqlIsntance {0}, db {1}", sqlInstance, database);
            string queryString = @"select domain, account_name from tbl_security_identity_cache, tbl_gss_groups, tbl_gss_group_membership
where tbl_gss_groups.display_name = 'SharePoint Web Application Services'
and tbl_gss_group_membership.parent_group_sid = tbl_gss_groups.sid
and tbl_security_identity_cache.sid = tbl_gss_group_membership.member_sid
";
            List<String> accounts = new List<String>();
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, database)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(queryString, conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string account = reader.GetString(0) + "\\" + reader.GetString(1);
                                TraceFormat.WriteLine(" Member Found '{0}'", account);
                                accounts.Add(account);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable retrieve wss service accounts.\n Exception: {0}", ex.Message);
            }
            return accounts;
        }

        /// <summary>
        /// Retrieve the AS server name from the configuration database
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetAnalysisServicesServer(string sqlInstance, string database)
        {
            string AsServerChildItem = @"Server\";
            string AsServerParentPath = @"#\Configuration\Database\BISANALYSIS DB\";

            return GetRegValueFromDb(sqlInstance, database, AsServerParentPath, AsServerChildItem);
        }

        /// <summary>
        /// Retrieve the AS server name from the configuration database
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetAnalysisServicesDatabaseName(string sqlInstance, string database)
        {
            string AsServerChildItem = @"DatabaseName\";
            string AsServerParentPath = @"#\Configuration\Database\BISANALYSIS DB\";

            return GetRegValueFromDb(sqlInstance, database, AsServerParentPath, AsServerChildItem);
        }

        /// <summary>
        /// Retrieve the warehouse connection string from the config db
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetWarehouseConnectionString(string sqlInstance, string database)
        {
            return GetRegValueFromDb(sqlInstance, database, RegistryItemParentPath(DatabaseCategory.Warehouse), RegistryItemChildItemString);
        }

        /// <summary>
        /// Retrieve the AS db connection string from the config db
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetAnalysisConnectionString(string sqlInstance, string database)
        {
            return GetRegValueFromDb(sqlInstance, database, RegistryItemParentPath(DatabaseCategory.Analysis), RegistryItemChildItemString);
        }

        /// <summary>
        /// Retrieve the ReportManagerUrl connection string from the config db
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetReportManagerUrl(string sqlInstance, string database)
        {
            return GetLocationByServiceType(sqlInstance, database, LocationMappingServiceType.ReportManagerUrl);
        }

        /// <summary>
        /// Retrieve the ReportWebServiceUrl connection string from the config db
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetReportWebServiceUrl(string sqlInstance, string database)
        {
            return GetLocationByServiceType(sqlInstance, database, LocationMappingServiceType.ReportWebServiceUrl);
        }

        /// <summary>
        /// Retrieve the WssAdminUrl connection string from the config db
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetWssAdminUrl(string sqlInstance, string database)
        {
            return GetLocationByServiceType(sqlInstance, database, LocationMappingServiceType.WssAdminUrl);
        }

        /// <summary>
        /// Retrieve the WssRootUrl connection string from the config db
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetWssRootUrl(string sqlInstance, string database)
        {
            return GetLocationByServiceType(sqlInstance, database, LocationMappingServiceType.WssRootUrl);
        }

        /// <summary>
        /// Retrieves the url from tbl_LocationMapping table that matches the specified ServiceType
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetLocationByServiceType(string sqlInstance, string database, LocationMappingServiceType serviceType)
        {
            TraceFormat.WriteLine("Retrieving {0} from sqlIsntance {1} db {2}", serviceType, sqlInstance, database);
            string queryString = string.Format(@"select tbl_LocationMapping.Location from tbl_LocationMapping where tbl_LocationMapping.ServiceType = '{0}'", serviceType);
            string url = string.Empty;
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, database)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(queryString, conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                url = reader.GetString(0);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to retrieve url.\n Exception: {0}", ex.Message);
            }
            return url;
        }

        /// <summary>
        /// Gets and returns the list of Team Project Collections using the COnfiguration db
        /// </summary>
        /// <param name="sqlInstance">sql Instance where the configuration db is stored</param>
        /// <param name="prefix">Configuration DB</param>
        /// <returns></returns>
        public static List<String> GetCollectionsFromConfigDb(string sqlInstance, string database)
        {
            TraceFormat.WriteLine("Getting the Collections from sqlIsntance {0}, db {1}", sqlInstance, database);
            List<String> collections = new List<String>();
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, database)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(@"SELECT tbl_CatalogResource.DisplayName FROM tbl_CatalogResource, tbl_CatalogResourceType
WHERE tbl_CatalogResourceType.DisplayName = 'Team Project Collection'
AND tbl_CatalogResourceType.Identifier = tbl_CatalogResource.ResourceType", conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string collection = reader.GetString(0);
                                TraceFormat.WriteLine(" Collection Found '{0}'", collection);
                                collections.Add(collection);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to Get The Collections.\n Exception: {0}", ex.Message);
            }
            return collections;
        }

        /// <summary>
        /// Helper methos to return the sqlinstance and the dbname of each collection in the Configuration db 
        /// </summary>
        /// <param name="sqlInstance">sql Instance where the configuration db is stored</param>
        /// <param name="database">Configuration DB</database>
        /// <returns>the sqlinstance and the dbname of each collection</returns>
        public static Dictionary<string, string> GetAllCollectionsFromConfigDb(string sqlInstance, string database)
        {
            TraceFormat.WriteLine("Retrieving the Collections from sqlIsntance {0}, db {1}", sqlInstance, database);

            List<String> connectionStrings = GetTPCConnectionStringFromConfigDb(sqlInstance, database);
            Dictionary<string, string> collections = new Dictionary<string, string>();
            foreach (string connString in connectionStrings)
            {
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connString);
                if (!string.Equals(builder.InitialCatalog, database))
                {
                    collections.Add(builder.InitialCatalog, builder.DataSource);
                }
            }
            return collections;
        }

        /// <summary>
        /// Retrieve the connection string of each collection in the Configuration db
        /// </summary>
        /// <param name="sqlInstance">sql Instance where the configuration db is stored</param>
        /// <param name="database">Configuration DB</database>
        /// <returns></returns>
        public static List<string> GetTPCConnectionStringFromConfigDb(string sqlInstance, string database)
        {
            TraceFormat.WriteLine("Getting the Collections from sqlIsntance {0}, db {1}", sqlInstance, database);
            List<String> connectionStrings = new List<String>();
            Dictionary<string, string> collections = new Dictionary<string, string>();
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, database)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(@"SELECT tbl_ServiceHost.ConnectionString FROM tbl_ServiceHost", conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string collection = reader.GetString(0);
                                TraceFormat.WriteLine(" Collection Found '{0}'", collection);
                                connectionStrings.Add(collection);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to Get The Collections.\n Exception: {0}", ex.Message);
            }
            return connectionStrings;
        }

        /// <summary>
        /// Starts the SQL Services across the deployment
        /// NOTE: temporary solution until MS.TF.Test.Admin.Setup can be referenced by the tests libraries
        /// </summary>
        /// <param name="deployment">VsDeployment</param>
        public static void StartSqlServicesInDeployment(string deploymentPath)
        {
            ExecutePhantomCommandInDeployment(string.Format(@"services /start /dt /rs /as /xml:{0}", deploymentPath));
        }

        /// <summary>
        /// Stops the SQL Services across the deployment
        /// NOTE: temporary solution until MS.TF.Test.Admin.Setup can be referenced by the tests libraries
        /// </summary>
        /// <param name="deployment">VsDeployment</param>
        public static void StopSqlServicesInDeployment(string deploymentPath)
        {
            ExecutePhantomCommandInDeployment(string.Format(@"services /stop /dt /as /rs /xml:{0}", deploymentPath));
        }

        /// <summary>
        /// Executes a phantom command using the deployment as a target
        /// </summary>
        /// <param name="args"></param>
        private static void ExecutePhantomCommandInDeployment(string args)
        {
            string adminOpsExe = FileSystemHelper.GetLocalPhantomPath();
            TraceFormat.WriteLine("Executing command {0} {1}", adminOpsExe, args);
            Executable exe = new Executable()
            {
                FileName = adminOpsExe,
                Arguments = args,
                SendOutputToTrace = true,
                WaitForExit = true,
            };
            exe.Run();
        }

        /// <summary>
        /// Returns a database name with the prefix.
        /// For whidbey\orcas\sharepoint dbs, it's just prefix+databasename
        /// For Dev10, it's Tfs_prefixConfiguration (for example)
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="databaseName"></param>
        /// <returns></returns>
        public static string PrefixedDatabaseName(string prefix, string databaseName)
        {
            databaseName = databaseName ?? String.Empty;

            if (databaseName.StartsWith("Tfs_", StringComparison.CurrentCultureIgnoreCase))
            {
                return databaseName.Insert(4, prefix);
            }
            else
            {
                return prefix + databaseName;
            }
        }

        /// <summary>
        /// Returns the prefix of the configuration database.
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="databaseName"></param>
        /// <returns></returns>
        public static string GetDatabasePrefix(string databaseName)
        {
            string prefix = string.Empty;
            if (!string.IsNullOrEmpty(databaseName))
            {
                //This works for only Dev10
                prefix = databaseName.Remove(databaseName.IndexOf(Tfs), Tfs.Length);
                prefix = prefix.Substring(0, prefix.LastIndexOf(Configuration));
            }
            return prefix;
        }

        /// <summary>
        /// Compare the DataSource and InitialCatalog of 2 connection strings
        /// </summary>
        /// <param name="conn1"></param>
        /// <param name="conn2"></param>
        /// <returns></returns>
        public static bool CompareConnectionStrings(string conn1, string conn2)
        {
            TraceFormat.WriteLine("Comapring connection strings: \"{0}\" with \"{1}\"", conn1, conn2);
            SqlConnectionStringBuilder builder1 = new SqlConnectionStringBuilder(conn1);
            SqlConnectionStringBuilder builder2 = new SqlConnectionStringBuilder(conn2);
            return ((String.Compare(builder1.InitialCatalog, builder2.InitialCatalog, true) == 0) && (String.Compare(builder1.DataSource, builder2.DataSource, true) == 0));
        }

        /// <summary>
        /// Validate all connection strings in the system
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// <returns></returns>
        public static bool AreAllConnectionStringsValid(string sqlInstance, string dbName)
        {
            bool success = IsConfigDbConnectionStringValid(sqlInstance, dbName)
                && AreAllTPCConnectionStringsValid(sqlInstance, dbName)
                && IsWarehouseDbConnectionStringValid(sqlInstance, dbName)
                && IsAnalysisDbConnectionStringValid(sqlInstance, dbName);
            return success;
        }

        /// <summary>
        /// 1- Get the warehouse connection string from table tbl_RegistryItem in the configuration database 
        /// 2- Compare it to the framework connection string 
        /// 3- Compare it to Tfs2010RSDataSource
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// <returns></returns>
        public static bool IsWarehouseDbConnectionStringValid(string sqlInstance, string dbName)
        {
            bool valid = true;
            string warehouseConnStr = GetWarehouseConnectionString(sqlInstance, dbName);
            if (string.IsNullOrEmpty(warehouseConnStr))
            {
                return valid;
            }

            //Warehouse Db is local to the configuration Db
            string webConfig = TfsInstanceUtilities.GetConfigurationDbConnectionStringFromWebConfig();
            if (!CompareConnectionStrings(webConfig, warehouseConnStr))
            {
                valid = false;
            }

            string warehouseConnStrFromDS = RSUtilities.GetConnectionStringFromDataSource(SqlUtilities.GetReportWebServiceUrl(sqlInstance, dbName), RSUtilities.Tfs2010RSDS);
            string warehouseConnStrFromConfigDb = warehouseConnStr;
            if (!CompareConnectionStrings(warehouseConnStrFromConfigDb, warehouseConnStrFromDS))
            {
                valid = false;
            }
            return valid;
        }

        /// <summary>
        /// 1- Get the cube connection string from table tbl_RegistryItem in the configuration database 
        /// 2- Compare it to Tfs2010OlapRSDataSource
        /// 3- Compare its datasource to the server name in tbl_RegistryItems
        /// 4- Compare its InitialCatalog to the database name in tbl_RegistryItems
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// <returns></returns>
        public static bool IsAnalysisDbConnectionStringValid(string sqlInstance, string dbName)
        {
            string olapConnStrFromDS = RSUtilities.GetConnectionStringFromDataSource(SqlUtilities.GetReportWebServiceUrl(sqlInstance, dbName), RSUtilities.Tfs2010OlapRSDS);
            string olapConnStrFromConfigDb = GetAnalysisConnectionString(sqlInstance, dbName);

            SqlConnectionStringBuilder asConnStr = new SqlConnectionStringBuilder(olapConnStrFromConfigDb);
            string asServer = GetAnalysisServicesServer(sqlInstance, dbName);
            string asDb = GetAnalysisServicesDatabaseName(sqlInstance, dbName);

            if (string.IsNullOrEmpty(olapConnStrFromConfigDb) && string.IsNullOrEmpty(asServer) && string.IsNullOrEmpty(asDb))
            {
                return true;
            }
            if (!(asServer.Equals(asConnStr.DataSource, StringComparison.OrdinalIgnoreCase) && asDb.Equals(asConnStr.InitialCatalog, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            if (!CompareConnectionStrings(olapConnStrFromDS, olapConnStrFromConfigDb))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 1- Get the framework connection string from web.config file 
        /// 2- Compare it with the framework connection string in tbl_registryItems
        /// 3- Compare it with the service host connection string in tbl_ServiceHost
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// <returns></returns>
        public static bool IsConfigDbConnectionStringValid(string sqlInstance, string dbName)
        {
            string connectionStringFromWebConfig = TfsInstanceUtilities.GetConfigurationDbConnectionStringFromWebConfig();
            return CompareConnectionStrings(connectionStringFromWebConfig, GetFrameworkConnectionString(sqlInstance, dbName)) &&
                CompareConnectionStrings(connectionStringFromWebConfig, GetInstanceConnectionStringFromServiceHost(sqlInstance, dbName));
        }

        /// <summary>
        /// 1- Get every collection connection string that exists in tbl_ServiceHost in configuration database 
        /// 2- Compare it with the connection strings tbl_RegistryItems in in the collection database
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// <returns></returns>
        public static bool AreAllTPCConnectionStringsValid(string sqlInstance, string database)
        {
            bool valid = true;
            string[] parentPaths = new string[]
            {
                RegistryItemParentPath(DatabaseCategory.Build),
                RegistryItemParentPath(DatabaseCategory.DeploymentRig),
                RegistryItemParentPath(DatabaseCategory.Framework),
                RegistryItemParentPath(DatabaseCategory.Integration),
                RegistryItemParentPath(DatabaseCategory.LabExecution),
                RegistryItemParentPath(DatabaseCategory.TestManagement),
                RegistryItemParentPath(DatabaseCategory.TestRig),
                RegistryItemParentPath(DatabaseCategory.VersionControl),
                RegistryItemParentPath(DatabaseCategory.WorkItem),
                RegistryItemParentPath(DatabaseCategory.WorkItemAttachment),
            };

            foreach (KeyValuePair<string, string> tpc in GetAllCollectionsFromConfigDb(sqlInstance, database))
            {
                // the key is dbname and the value is the identifier
                //validate that the tbl_ServiceHost in config db has the same connection string as the table tbl_RegistryItems in the tpc db
                string tpcInConfigDB = GetServiceHostConnectionString(sqlInstance, database, GetCollectionName(tpc.Key));

                foreach (string parentPath in parentPaths)
                {
                    TraceFormat.WriteLine("Validating collection {0}'s connection string in db category {1}...", tpc.Value, parentPath);
                    if (!CompareConnectionStrings(tpcInConfigDB, GetRegValueFromDb(tpc.Value, tpc.Key, parentPath, RegistryItemChildItemString)))
                    {
                        TraceFormat.WriteLine("Validating collection {0}'s connection string in db category {1} failed", tpc.Value, parentPath);
                        valid = false;
                    }
                }
            }
            return valid;
        }

        public static string GetFrameworkConnectionString(string sqlInstance, string database)
        {
            return GetRegValueFromDb(sqlInstance, database, SqlUtilities.RegistryItemParentPath(DatabaseCategory.Framework), RegistryItemChildItemString);
        }

        public static string GetCollectionName(string collectionDbName)
        {
            return collectionDbName.Substring(collectionDbName.IndexOf("Tfs_") + 4);
        }

        /// <summary>
        /// Retrieves the connection string for the configuration database from tbl_ServiceHost
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <returns></returns>
        public static string GetInstanceConnectionStringFromServiceHost(string sqlInstance, string database)
        {
            return GetServiceHostConnectionString(sqlInstance, database, "TEAM FOUNDATION");
        }

        /// <summary>
        /// Retrieves the connection strings fro config db and collection dbs from service host table
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetServiceHostConnectionString(string sqlInstance, string database, string name)
        {
            TraceFormat.WriteLine("Retrieving connection string for {0} using sqlIsntance {1} and db {2} from tbl_ServiceHost", name, sqlInstance, database);
            string queryString = string.Format(@"SELECT ConnectionString FROM tbl_Database LEFT JOIN tbl_ServiceHost ON tbl_ServiceHost.DatabaseId = tbl_Database.DatabaseId WHERE tbl_ServiceHost.Name = '{0}'", name);

            string connStr = string.Empty;
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, database)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(queryString, conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                connStr = reader.GetString(0);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to retrieve connection string.\n Exception: {0}", ex.Message);
            }
            return connStr;
        }

        /// <summary>
        /// Retrieves the registry items for the specified parentpath and childitem 
        /// </summary>
        /// <param name="sqlInstance"></param>
        /// <param name="database"></param>
        /// <param name="parentPath"></param>
        /// <param name="childItem"></param>
        /// <returns></returns>
        public static string GetRegValueFromDb(string sqlInstance, string database, string parentPath, string childItem)
        {
            TraceFormat.WriteLine("Retrieving registry value using sqlIsntance {0} and db {1}", sqlInstance, database);
            string queryString = string.Format(@"select tbl_RegistryItems.RegValue from tbl_RegistryItems where tbl_RegistryItems.ParentPath = '{0}' and tbl_RegistryItems.ChildItem = '{1}'", parentPath, childItem);
            string rsServer = string.Empty;
            List<String> accounts = new List<String>();
            try
            {
                using (SqlConnection conn = new SqlConnection(GetConnectionString(sqlInstance, database)))
                {
                    conn.Open();
                    using (SqlCommand query = new SqlCommand(queryString, conn))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                rsServer = reader.GetString(0);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TraceFormat.WriteError("Unable to retrieve registry value.\n Exception: {0}", ex.Message);
            }
            return rsServer;
        }

        /// <summary>
        /// Check if the master and msdb dbs have the sys messages used by Tfs
        /// We are not only checking for the message with the ID 40000
        /// </summary>
        /// <param name="sqlInstance">sql Instance where the configuration db is stored</param>
        /// <returns></returns>
        /// ////
        public static bool MasterHasSysMessages(string sqlInstance)
        {
            string errorID = "400000";
            string query = String.Format("SELECT Count(*) from dbo.sysmessages WHERE error='{0}'", errorID);

            int count = 0;

            try
            {
                count = (int)SqlExecuteScalar(sqlInstance, "master", query, 5);
            }
            catch (Exception e)
            {
                throw new Exception(String.Format("error {0} does not exist in master. Exception {1}", errorID, e.Message));
            }
            return count == 1;
        }

        /// <summary>
        /// check if a valid user is the Database Owner for tfs databases
        /// </summary>
        /// <param name="user">the user should be in the form domain\user</param>
        /// <param name="instance">The SQL instance</param>
        /// <param name="db">the Database name</param>
        /// <returns></returns>
        public static bool DbsHaveValidOwners(string user, string sqlInstance, string dbName)
        {
            bool success = true;

            string warehouseConnStr = GetWarehouseConnectionString(sqlInstance, dbName);
            SqlConnectionStringBuilder warehouse = new SqlConnectionStringBuilder(warehouseConnStr);

            if (!(HasValidOwner(user, sqlInstance, dbName) //validate config DB owner
                && (!string.IsNullOrEmpty(warehouseConnStr)) && HasValidOwner(user, warehouse.DataSource, warehouse.InitialCatalog))) //validate Tfs_Warehouse owner
            {
                return false;
            }

            //validate TPCs owners
            foreach (KeyValuePair<string, string> tpc in GetAllCollectionsFromConfigDb(sqlInstance, dbName))
            {
                if (!HasValidOwner(user, tpc.Value, tpc.Key))
                {
                    success = false;
                    break;
                }
            }
            return success;
        }

        /// <summary>
        /// Check if the specified dbname has a valid owner
        /// </summary>
        /// <param name="user"></param>
        /// <param name="sqlInstance"></param>
        /// <param name="dbName"></param>
        /// <returns></returns>
        public static bool HasValidOwner(string user, string sqlInstance, string dbName)
        {
            string currentUser = WindowsIdentity.GetCurrent().Name;
            bool success = true;
            if (!IsDatabaseOwner(user, sqlInstance, dbName) && !IsDatabaseOwner(currentUser, sqlInstance, dbName) && !IsDatabaseOwner("sa", sqlInstance, dbName))
            {
                TraceFormat.WriteLine("The owner of db {0} is invalid", dbName);
                success = false;
            }
            return success;
        }
        /// <summary>
        /// Finds if the SQL component is installed.
        /// </summary>
        /// <param name="instanceName"></param>
        /// <param name="component">Components strings for db, as, and rs are respectively SQL, OLAP, and RS</param>
        /// <returns></returns>
        public static bool IsComponentInstalled(SqlComponent sql)
        {
            return GetRegistryInstanceName(sql) != null;
        }

        /// <summary>
        /// Returns a string like MSSQL11.MSSQLSERVER or null which can be used to
        /// (a) identify if the component is installed (if this method retuns null)
        /// (b) calculate the registry key needed to get the TCP Port number it is using
        /// </summary>
        /// <param name="sql"></param>
        /// <returns></returns>
        public static string GetRegistryInstanceName(SqlComponent sql)
        {
            // TODO: Each object should really have an IsInstalled which would cover this.
            string component = string.Empty;
            if (sql is SqlDatabaseComponent)
            {
                component = "SQL";
            }
            else if (sql is SqlAnalysisServiceComponent)
            {
                component = "OLAP";
            }
            else if (sql is SqlReportingServiceComponent)
            {
                component = "RS";
            }
            else
            {
                throw new NotImplementedException("No SQL component specified!");
            }

            string keyPath = string.Format(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\{0}", component);
            object keyValue = RegistryUtilities.GetRegistryKeyValue(Registry.LocalMachine, keyPath, sql.InstanceOrDefaultName);

            TraceFormat.WriteLine("SQL component {0} keyvalue ==  {1}", sql, keyValue);
            return (string)keyValue;
        }

        public static int GetPortNumber(SqlDatabaseComponent dt)
        {
            string instanceName = GetRegistryInstanceName(dt);
            string keyPath = string.Format(@"SOFTWARE\Microsoft\Microsoft SQL Server\{0}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll", instanceName);
            string keyValue = (string)RegistryUtilities.GetRegistryKeyValue(Registry.LocalMachine, keyPath, "TcpPort");
            if (string.IsNullOrEmpty(keyValue))
            {
                keyValue = (string)RegistryUtilities.GetRegistryKeyValue(Registry.LocalMachine, keyPath, "TcpDynamicPorts");
            }

            TraceFormat.WriteLine("SQL component {0} port ==  {1}", dt, keyValue);

            return (int)Int32.Parse(keyValue);
        }

        /// <summary>
        /// Get the FQDN for a sql azure server
        /// </summary>
        /// <param name="sqlInstance">sql azure server name</param>
        /// <returns></returns>
        public static string GetSqlServerFQDN(string sqlInstance)
        {
            if (!String.IsNullOrEmpty(sqlInstance))
            {
                if (!sqlInstance.StartsWith("tcp:"))
                {
                    sqlInstance = "tcp:" + sqlInstance;
                }

                if (!sqlInstance.EndsWith(".database.windows.net"))
                {
                    sqlInstance = sqlInstance + ".database.windows.net";
                }
            }
            return sqlInstance;
        }

        /// <summary>
        /// Returns true a connection can be established with the specified connection string 
        /// </summary>
        /// <param name="dbConnectionString">database connection string</param>
        /// <returns>true when the connection succeeds, false otherwise</returns>
        public static bool CanConnect(string dbConnectionString)
        {
            bool exists = false;
            try
            {
                using (SqlConnection connection = new SqlConnection(dbConnectionString))
                {
                    connection.Open();
                    exists = true;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("Unable to establish a connection to {0}, Exception {1}", dbConnectionString, ex.ToString());
            }
            return exists;
        }

        /// <summary>
        /// Gets the list of stored procedures in the database
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static List<String> GetDatabaseSprocs(string connectionString)
        {
            return GetDatabaseSysObject(connectionString, "P");
        }

        /// <summary>
        /// Gets the list of tables in the database
        /// </summary>
        /// <param name="dbConnectionString"></param>
        /// <returns></returns>
        public static List<String> GetDatabaseTables(string connectionString)
        {
            return GetDatabaseSysObject(connectionString, "U");
        }

        /// <summary>
        /// Gets the list of views in the database
        /// </summary>
        /// <param name="dbConnectionString"></param>
        /// <returns></returns>
        public static List<String> GetDatabaseViews(string connectionString)
        {
            return GetDatabaseSysObject(connectionString, "V");
        }

        /// <summary>
        ///SELECT name FROM sysobjects WHERE xtype = 'U' -- Tables
        ///SELECT name FROM sysobjects WHERE xtype = 'V' -- Views
        ///SELECT name FROM sysobjects WHERE xtype = 'P' -- Stored Procedures
        /// </summary>
        /// <param name="objectType"></param>
        /// <returns></returns>
        private static List<string> GetDatabaseSysObject(string connectionString, string objectType)
        {
            string statement = string.Format(@"SELECT name FROM sysobjects WHERE xtype = '{0}'", objectType);
            List<String> objects = new List<String>();

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (SqlCommand query = new SqlCommand(statement, connection))
                    {
                        using (SqlDataReader reader = query.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string collection = reader.GetString(0);
                                TraceFormat.WriteLine(" Collection Found '{0}'", collection);
                                objects.Add(collection);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("Unable to get the object, Exception {0}", ex.ToString());
            }
            return objects;
        }

        internal static void SetSql2005AppCompatibilityFlags()
        {
            // Add regkeys to disable app compat warnings for SQL Server 2005
            if (Utilities.GetLocalOperatingSystem().Release >= OperatingSystemRelease.Vista)
            {
                TraceFormat.WriteLine("Setting SQL2005 AppCompatibilityFlags on Longhorn");
                RegistryKey appCompatKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags", true);
                if (appCompatKey != null)
                {
                    // SQL Server 2k5
                    appCompatKey.SetValue("{3d06c673-5e8a-41c0-b47f-3c3ca0a22e67}", 119, RegistryValueKind.DWord);
                    appCompatKey.SetValue("{1ba7896c-e86c-4130-9c4b-d457962f9186}", 119, RegistryValueKind.DWord);
                    appCompatKey.SetValue("{2a0da30d-846f-4680-89da-2dbc457d7b44}", 119, RegistryValueKind.DWord);
                    appCompatKey.SetValue("{319b29ae-7427-4fbc-9355-22df056c27a4}", 119, RegistryValueKind.DWord);
                    appCompatKey.SetValue("{5337145e-575b-46ec-bfa0-0b034c6b51e4}", 119, RegistryValueKind.DWord);
                    appCompatKey.SetValue("{917c7762-3aa8-4fb3-9ae6-ab80bbc398ad}", 119, RegistryValueKind.DWord);
                    // VS IDE
                    appCompatKey.SetValue("{5f66dbae-cad3-468a-83d0-77ace8abc1f6}", 119, RegistryValueKind.DWord);
                    appCompatKey.SetValue("{7a611f25-5150-4395-855a-88ee9ab1176e}", 119, RegistryValueKind.DWord);
                    appCompatKey.Close();
                }
            }
        }

        internal static void SetSql2008AppCompatibilityFlags()
        {
            // Add regkeys to disable app compat warnings for SQL Server 2008
            if (Utilities.GetLocalOperatingSystem().Release >= OperatingSystemRelease.Windows7)
            {
                TraceFormat.WriteLine("Setting SQL 2008 AppCompatibilityFlags");
                using (RegistryKey appCompatKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags"))
                {
                    if (appCompatKey != null)
                    {
                        // SQL Server 2k8
                        appCompatKey.SetValue("{3f27639b-cb19-45c3-90ef-aab214ed56df}", 119, RegistryValueKind.DWord);
                        appCompatKey.SetValue("{8fc3ed31-27be-4ac7-bfe9-abd8d0d0b2ef}", 119, RegistryValueKind.DWord);
                        appCompatKey.SetValue("{b2f0ba19-9157-47a1-bc89-20fe7c43625e}", 119, RegistryValueKind.DWord);
                        appCompatKey.SetValue("{b3fbd80c-bf8f-4097-80c8-160659927444}", 119, RegistryValueKind.DWord);
                        appCompatKey.SetValue("{5337145e-575b-46ec-bfa0-0b034c6b51e4}", 119, RegistryValueKind.DWord);
                        appCompatKey.SetValue("{e8d4f5fc-b018-451e-84d8-261112d5b450}", 119, RegistryValueKind.DWord);
                        appCompatKey.SetValue("{f2d3ae3a-bfcc-45e2-bf63-178d1db34294}", 119, RegistryValueKind.DWord);
                        appCompatKey.SetValue("{45da5a8b-67b5-4896-86b7-a2e838aee035}", 119, RegistryValueKind.DWord);
                    }
                }
            }
        }
		
	public class test
	{	
		public enum PublishDbData
		{
		   DbName
		}
	}
	public class test1
	{	
		public enum PublishDbData
		{
		   DbName
		}
	}
	public class test2
	{	
		public enum PublishDbData
		{
		   DbName
		}
	}
	public class test3
	{	
		public enum PublishDbData
		{
		   DbName
		}
	}
	public class test4
	{	
		public enum PublishDbData
		{
		   DbName
		}
	}
	public class test5
	{	
		public enum PublishDbData
		{
		   DbName
		}
	}
	public class test6
	{		
		void PublishDbData(string eventName, int componentId, string value);

		void PublishDbData(string eventName, int componentId, double value);
		
		void PublishDbData(string eventName, int componentId);
		
		void PublishDbData(string eventName, double value);
		
		void PublishDbData(int componentId, double value);
		
		void PublishDbData();
		
		void PublishDbData(string eventName);
		
		void PublishDbData(int componentId);
		
		void PublishDbData(double value);
		
		void PublishDbData(double eventName, double componentId, double value);
		
		void PublishDbData(string eventName, string componentId, string value);
		
		void PublishDbData(int eventName, int componentId, int value);
		
		void PublishDbData(string eventName, string componentId, double value);
		
		void PublishDbData(string eventName, string componentId, int value);
		
		void PublishDbData(int eventName, int componentId, double value);
		
		void PublishDbData(int eventName, int componentId, string value);
		
		void PublishDbData(double eventName, double componentId, string value);
		
		void PublishDbData(double eventName, double componentId, int value);
		
		void PublishDbData(string eventName, int componentId, string value);
		
		void PublishDbData(string eventName, double componentId, string value);
		
		void PublishDbData(double eventName, int componentId, double value);
		
		void PublishDbData(double eventName, string componentId, double value);
		
		void PublishDbData(int eventName, string componentId, int value);
		
		void PublishDbData(int eventName, double componentId, int value);
		
		void PublishDbData(string eventName, int componentId, double value);
		
		void PublishDbData(string eventId, string eventName, int componentId, string value);

		void PublishDbData(string eventId, string eventName, int componentId, double value);
		
		void PublishDbData(string eventId, double eventName, double componentId, double value);
		
		void PublishDbData(string eventId, string eventName, string componentId, string value);
		
		void PublishDbData(string eventId, int eventName, int componentId, int value);
		
		void PublishDbData(string eventId, string eventName, string componentId, double value);
		
		void PublishDbData(string eventId, string eventName, string componentId, int value);
		
		void PublishDbData(string eventId, int eventName, int componentId, double value);
		
		void PublishDbData(string eventId, int eventName, int componentId, string value);
		
		void PublishDbData(string eventId, double eventName, double componentId, string value);
		
		void PublishDbData(string eventId, double eventName, double componentId, int value);
		
		void PublishDbData(string eventId, string eventName, int componentId, string value);
		
		void PublishDbData(string eventId, string eventName, double componentId, string value);
		
		void PublishDbData(string eventId, double eventName, int componentId, double value);
		
		void PublishDbData(string eventId, double eventName, string componentId, double value);
		
		void PublishDbData(string eventId, int eventName, string componentId, int value);
		
		void PublishDbData(string eventId, int eventName, double componentId, int value);
		
		void PublishDbData(string eventId, string eventName, int componentId, double value);
		
		void PublishDbData(string eventId, string eventId, string eventName, int componentId, string value);

		void PublishDbData(string eventId, string eventId, string eventName, int componentId, double value);
		
		void PublishDbData(string eventId, string eventId, double eventName, double componentId, double value);
		
		void PublishDbData(string eventId, string eventId, string eventName, string componentId, string value);
		
		void PublishDbData(string eventId, string eventId, int eventName, int componentId, int value);
		
		void PublishDbData(string eventId, string eventId, string eventName, string componentId, double value);
		
		void PublishDbData(string eventId, string eventId, string eventName, string componentId, int value);
		
		void PublishDbData(string eventId, string eventId, int eventName, int componentId, double value);
		
		void PublishDbData(string eventId, string eventId, int eventName, int componentId, string value);
		
		void PublishDbData(string eventId, string eventId, double eventName, double componentId, string value);
		
		void PublishDbData(string eventId, string eventId, double eventName, double componentId, int value);
		
		void PublishDbData(string eventId, string eventId, string eventName, int componentId, string value);
		
		void PublishDbData(string eventId, string eventId, string eventName, double componentId, string value);
		
		void PublishDbData(string eventId, string eventId, double eventName, int componentId, double value);
		
		void PublishDbData(string eventId, string eventId, double eventName, string componentId, double value);
		
		void PublishDbData(string eventId, string eventId, int eventName, string componentId, int value);
		
		void PublishDbData(string eventId, string eventId, int eventName, double componentId, int value);
		
		void PublishDbData(string eventId, string eventId, string eventName, int componentId, double value);
		
		void PublishDbData(int eventId, string eventName, int componentId, string value);

		void PublishDbData(int eventId, string eventName, int componentId, double value);
		
		void PublishDbData(int eventId, double eventName, double componentId, double value);
		
		void PublishDbData(int eventId, string eventName, string componentId, string value);
		
		void PublishDbData(int eventId, int eventName, int componentId, int value);
		
		void PublishDbData(int eventId, string eventName, string componentId, double value);
		
		void PublishDbData(int eventId, string eventName, string componentId, int value);
		
		void PublishDbData(int eventId, int eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventName, int componentId, string value);
		
		void PublishDbData(int eventId, double eventName, double componentId, string value);
		
		void PublishDbData(int eventId, double eventName, double componentId, int value);
		
		void PublishDbData(int eventId, string eventName, int componentId, string value);
		
		void PublishDbData(int eventId, string eventName, double componentId, string value);
		
		void PublishDbData(int eventId, double eventName, int componentId, double value);
		
		void PublishDbData(int eventId, double eventName, string componentId, double value);
		
		void PublishDbData(int eventId, int eventName, string componentId, int value);
		
		void PublishDbData(int eventId, int eventName, double componentId, int value);
		
		void PublishDbData(int eventId, string eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, string eventName, int componentId, string value);

		void PublishDbData(int eventId, int eventId, string eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, double eventName, double componentId, double value);
		
		void PublishDbData(int eventId, int eventId, string eventName, string componentId, string value);
		
		void PublishDbData(int eventId, int eventId, int eventName, int componentId, int value);
		
		void PublishDbData(int eventId, int eventId, string eventName, string componentId, double value);
		
		void PublishDbData(int eventId, int eventId, string eventName, string componentId, int value);
		
		void PublishDbData(int eventId, int eventId, int eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventName, int componentId, string value);
		
		void PublishDbData(int eventId, int eventId, double eventName, double componentId, string value);
		
		void PublishDbData(int eventId, int eventId, double eventName, double componentId, int value);
		
		void PublishDbData(int eventId, int eventId, string eventName, int componentId, string value);
		
		void PublishDbData(int eventId, int eventId, string eventName, double componentId, string value);
		
		void PublishDbData(int eventId, int eventId, double eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, double eventName, string componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventName, string componentId, int value);
		
		void PublishDbData(int eventId, int eventId, int eventName, double componentId, int value);
		
		void PublishDbData(int eventId, int eventId, string eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, string value);

		void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventId, double eventName, double componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, string componentId, string value);
		
		void PublishDbData(int eventId, int eventId, int eventId, int eventName, int componentId, int value);
		
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, string componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, string componentId, int value);
		
		void PublishDbData(int eventId, int eventId, int eventId, int eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventId, int eventName, int componentId, string value);
		
		void PublishDbData(int eventId, int eventId, int eventId, double eventName, double componentId, string value);
		
		void PublishDbData(int eventId, int eventId, int eventId, double eventName, double componentId, int value);
		
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, string value);
		
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, double componentId, string value);
		
		void PublishDbData(int eventId, int eventId, int eventId, double eventName, int componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventId, double eventName, string componentId, double value);
		
		void PublishDbData(int eventId, int eventId, int eventId, int eventName, string componentId, int value);
		
		void PublishDbData(int eventId, int eventId, int eventId, int eventName, double componentId, int value);
		
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, double value);
		
			void PublishDbData(double eventId, string eventName, int componentId, string value);

		void PublishDbData(double eventId, string eventName, int componentId, double value);
		
		void PublishDbData(double eventId, double eventName, double componentId, double value);
		
		void PublishDbData(double eventId, string eventName, string componentId, string value);
		
		void PublishDbData(double eventId, int eventName, int componentId, int value);
		
		void PublishDbData(double eventId, string eventName, string componentId, double value);
		
		void PublishDbData(double eventId, string eventName, string componentId, int value);
		
		void PublishDbData(double eventId, int eventName, int componentId, double value);
		
		void PublishDbData(double eventId, int eventName, int componentId, string value);
		
		void PublishDbData(double eventId, double eventName, double componentId, string value);
		
		void PublishDbData(double eventId, double eventName, double componentId, int value);
		
		void PublishDbData(double eventId, string eventName, int componentId, string value);
		
		void PublishDbData(double eventId, string eventName, double componentId, string value);
		
		void PublishDbData(double eventId, double eventName, int componentId, double value);
		
		void PublishDbData(double eventId, double eventName, string componentId, double value);
		
		void PublishDbData(double eventId, int eventName, string componentId, int value);
		
		void PublishDbData(double eventId, int eventName, double componentId, int value);
		
		void PublishDbData(double eventId, string eventName, int componentId, double value);
		
			void PublishDbData(double eventId1, double eventId, string eventName, int componentId, string value);

		void PublishDbData(double eventId1, double eventId, string eventName, int componentId, double value);
		
		void PublishDbData(double eventId1, double eventId, double eventName, double componentId, double value);
		
		void PublishDbData(double eventId1, double eventId, string eventName, string componentId, string value);
		
		void PublishDbData(double eventId1, double eventId, int eventName, int componentId, int value);
		
		void PublishDbData(double eventId1, double eventId, string eventName, string componentId, double value);
		
		void PublishDbData(double eventId1, double eventId, string eventName, string componentId, int value);
		
		void PublishDbData(double eventId1, double eventId, int eventName, int componentId, double value);
		
		void PublishDbData(double eventId1, double eventId, int eventName, int componentId, string value);
		
		void PublishDbData(double eventId1, double eventId, double eventName, double componentId, string value);
		
		void PublishDbData(double eventId1, double eventId, double eventName, double componentId, int value);
		
		void PublishDbData(double eventId1, double eventId, string eventName, int componentId, string value);
		
		void PublishDbData(double eventId1, double eventId, string eventName, double componentId, string value);
		
		void PublishDbData(double eventId1, double eventId, double eventName, int componentId, double value);
		
		void PublishDbData(double eventId1, double eventId, double eventName, string componentId, double value);
		
		void PublishDbData(double eventId1, double eventId, int eventName, string componentId, int value);
		
		void PublishDbData(double eventId1, double eventId, int eventName, double componentId, int value);
		
		void PublishDbData(double eventId1, double eventId, string eventName, int componentId, double value);
		
	}
	
	void PublishDbData(string eventName, int componentId, string value);

	void PublishDbData(string eventName, int componentId, double value);
	
	void PublishDbData(string eventName, int componentId);
	
	void PublishDbData(string eventName, double value);
	
	void PublishDbData(int componentId, double value);
	
	void PublishDbData();
	
	void PublishDbData(string eventName);
	
	void PublishDbData(int componentId);
	
	void PublishDbData(double value);
	
	void PublishDbData(double eventName, double componentId, double value);
	
	void PublishDbData(string eventName, string componentId, string value);
	
	void PublishDbData(int eventName, int componentId, int value);
	
	void PublishDbData(string eventName, string componentId, double value);
	
	void PublishDbData(string eventName, string componentId, int value);
	
	void PublishDbData(int eventName, int componentId, double value);
	
	void PublishDbData(int eventName, int componentId, string value);
	
	void PublishDbData(double eventName, double componentId, string value);
	
	void PublishDbData(double eventName, double componentId, int value);
	
	void PublishDbData(string eventName, int componentId, string value);
	
	void PublishDbData(string eventName, double componentId, string value);
	
	void PublishDbData(double eventName, int componentId, double value);
	
	void PublishDbData(double eventName, string componentId, double value);
	
	void PublishDbData(int eventName, string componentId, int value);
	
	void PublishDbData(int eventName, double componentId, int value);
	
	void PublishDbData(string eventName, int componentId, double value);
	
	void PublishDbData(string eventId, string eventName, int componentId, string value);

	void PublishDbData(string eventId, string eventName, int componentId, double value);
	
	void PublishDbData(string eventId, double eventName, double componentId, double value);
	
	void PublishDbData(string eventId, string eventName, string componentId, string value);
	
	void PublishDbData(string eventId, int eventName, int componentId, int value);
	
	void PublishDbData(string eventId, string eventName, string componentId, double value);
	
	void PublishDbData(string eventId, string eventName, string componentId, int value);
	
	void PublishDbData(string eventId, int eventName, int componentId, double value);
	
	void PublishDbData(string eventId, int eventName, int componentId, string value);
	
	void PublishDbData(string eventId, double eventName, double componentId, string value);
	
	void PublishDbData(string eventId, double eventName, double componentId, int value);
	
	void PublishDbData(string eventId, string eventName, int componentId, string value);
	
	void PublishDbData(string eventId, string eventName, double componentId, string value);
	
	void PublishDbData(string eventId, double eventName, int componentId, double value);
	
	void PublishDbData(string eventId, double eventName, string componentId, double value);
	
	void PublishDbData(string eventId, int eventName, string componentId, int value);
	
	void PublishDbData(string eventId, int eventName, double componentId, int value);
	
	void PublishDbData(string eventId, string eventName, int componentId, double value);
	
	void PublishDbData(string eventId, string eventId, string eventName, int componentId, string value);

	void PublishDbData(string eventId, string eventId, string eventName, int componentId, double value);
	
	void PublishDbData(string eventId, string eventId, double eventName, double componentId, double value);
	
	void PublishDbData(string eventId, string eventId, string eventName, string componentId, string value);
	
	void PublishDbData(string eventId, string eventId, int eventName, int componentId, int value);
	
	void PublishDbData(string eventId, string eventId, string eventName, string componentId, double value);
	
	void PublishDbData(string eventId, string eventId, string eventName, string componentId, int value);
	
	void PublishDbData(string eventId, string eventId, int eventName, int componentId, double value);
	
	void PublishDbData(string eventId, string eventId, int eventName, int componentId, string value);
	
	void PublishDbData(string eventId, string eventId, double eventName, double componentId, string value);
	
	void PublishDbData(string eventId, string eventId, double eventName, double componentId, int value);
	
	void PublishDbData(string eventId, string eventId, string eventName, int componentId, string value);
	
	void PublishDbData(string eventId, string eventId, string eventName, double componentId, string value);
	
	void PublishDbData(string eventId, string eventId, double eventName, int componentId, double value);
	
	void PublishDbData(string eventId, string eventId, double eventName, string componentId, double value);
	
	void PublishDbData(string eventId, string eventId, int eventName, string componentId, int value);
	
	void PublishDbData(string eventId, string eventId, int eventName, double componentId, int value);
	
	void PublishDbData(string eventId, string eventId, string eventName, int componentId, double value);
	
	void PublishDbData(int eventId, string eventName, int componentId, string value);

	void PublishDbData(int eventId, string eventName, int componentId, double value);
	
	void PublishDbData(int eventId, double eventName, double componentId, double value);
	
	void PublishDbData(int eventId, string eventName, string componentId, string value);
	
	void PublishDbData(int eventId, int eventName, int componentId, int value);
	
	void PublishDbData(int eventId, string eventName, string componentId, double value);
	
	void PublishDbData(int eventId, string eventName, string componentId, int value);
	
	void PublishDbData(int eventId, int eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventName, int componentId, string value);
	
	void PublishDbData(int eventId, double eventName, double componentId, string value);
	
	void PublishDbData(int eventId, double eventName, double componentId, int value);
	
	void PublishDbData(int eventId, string eventName, int componentId, string value);
	
	void PublishDbData(int eventId, string eventName, double componentId, string value);
	
	void PublishDbData(int eventId, double eventName, int componentId, double value);
	
	void PublishDbData(int eventId, double eventName, string componentId, double value);
	
	void PublishDbData(int eventId, int eventName, string componentId, int value);
	
	void PublishDbData(int eventId, int eventName, double componentId, int value);
	
	void PublishDbData(int eventId, string eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventId, string eventName, int componentId, string value);

	void PublishDbData(int eventId, int eventId, string eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventId, double eventName, double componentId, double value);
	
	void PublishDbData(int eventId, int eventId, string eventName, string componentId, string value);
	
	void PublishDbData(int eventId, int eventId, int eventName, int componentId, int value);
	
	void PublishDbData(int eventId, int eventId, string eventName, string componentId, double value);
	
	void PublishDbData(int eventId, int eventId, string eventName, string componentId, int value);
	
	void PublishDbData(int eventId, int eventId, int eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventName, int componentId, string value);
	
	void PublishDbData(int eventId, int eventId, double eventName, double componentId, string value);
	
	void PublishDbData(int eventId, int eventId, double eventName, double componentId, int value);
	
	void PublishDbData(int eventId, int eventId, string eventName, int componentId, string value);
	
	void PublishDbData(int eventId, int eventId, string eventName, double componentId, string value);
	
	void PublishDbData(int eventId, int eventId, double eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventId, double eventName, string componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventName, string componentId, int value);
	
	void PublishDbData(int eventId, int eventId, int eventName, double componentId, int value);
	
	void PublishDbData(int eventId, int eventId, string eventName, int componentId, double value);
	
		void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, string value);

	void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventId, double eventName, double componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventId, string eventName, string componentId, string value);
	
	void PublishDbData(int eventId, int eventId, int eventId, int eventName, int componentId, int value);
	
	void PublishDbData(int eventId, int eventId, int eventId, string eventName, string componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventId, string eventName, string componentId, int value);
	
	void PublishDbData(int eventId, int eventId, int eventId, int eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventId, int eventName, int componentId, string value);
	
	void PublishDbData(int eventId, int eventId, int eventId, double eventName, double componentId, string value);
	
	void PublishDbData(int eventId, int eventId, int eventId, double eventName, double componentId, int value);
	
	void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, string value);
	
	void PublishDbData(int eventId, int eventId, int eventId, string eventName, double componentId, string value);
	
	void PublishDbData(int eventId, int eventId, int eventId, double eventName, int componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventId, double eventName, string componentId, double value);
	
	void PublishDbData(int eventId, int eventId, int eventId, int eventName, string componentId, int value);
	
	void PublishDbData(int eventId, int eventId, int eventId, int eventName, double componentId, int value);
	
	void PublishDbData(int eventId, int eventId, int eventId, string eventName, int componentId, double value);
	
		void PublishDbData(double eventId, string eventName, int componentId, string value);

	void PublishDbData(double eventId, string eventName, int componentId, double value);
	
	void PublishDbData(double eventId, double eventName, double componentId, double value);
	
	void PublishDbData(double eventId, string eventName, string componentId, string value);
	
	void PublishDbData(double eventId, int eventName, int componentId, int value);
	
	void PublishDbData(double eventId, string eventName, string componentId, double value);
	
	void PublishDbData(double eventId, string eventName, string componentId, int value);
	
	void PublishDbData(double eventId, int eventName, int componentId, double value);
	
	void PublishDbData(double eventId, int eventName, int componentId, string value);
	
	void PublishDbData(double eventId, double eventName, double componentId, string value);
	
	void PublishDbData(double eventId, double eventName, double componentId, int value);
	
	void PublishDbData(double eventId, string eventName, int componentId, string value);
	
	void PublishDbData(double eventId, string eventName, double componentId, string value);
	
	void PublishDbData(double eventId, double eventName, int componentId, double value);
	
	void PublishDbData(double eventId, double eventName, string componentId, double value);
	
	void PublishDbData(double eventId, int eventName, string componentId, int value);
	
	void PublishDbData(double eventId, int eventName, double componentId, int value);
	
	void PublishDbData(double eventId, string eventName, int componentId, double value);
	
		void PublishDbData(double eventId1, double eventId, string eventName, int componentId, string value);

	void PublishDbData(double eventId1, double eventId, string eventName, int componentId, double value);
	
	void PublishDbData(double eventId1, double eventId, double eventName, double componentId, double value);
	
	void PublishDbData(double eventId1, double eventId, string eventName, string componentId, string value);
	
	void PublishDbData(double eventId1, double eventId, int eventName, int componentId, int value);
	
	void PublishDbData(double eventId1, double eventId, string eventName, string componentId, double value);
	
	void PublishDbData(double eventId1, double eventId, string eventName, string componentId, int value);
	
	void PublishDbData(double eventId1, double eventId, int eventName, int componentId, double value);
	
	void PublishDbData(double eventId1, double eventId, int eventName, int componentId, string value);
	
	void PublishDbData(double eventId1, double eventId, double eventName, double componentId, string value);
	
	void PublishDbData(double eventId1, double eventId, double eventName, double componentId, int value);
	
	void PublishDbData(double eventId1, double eventId, string eventName, int componentId, string value);
	
	void PublishDbData(double eventId1, double eventId, string eventName, double componentId, string value);
	
	void PublishDbData(double eventId1, double eventId, double eventName, int componentId, double value);
	
	void PublishDbData(double eventId1, double eventId, double eventName, string componentId, double value);
	
	void PublishDbData(double eventId1, double eventId, int eventName, string componentId, int value);
	
	void PublishDbData(double eventId1, double eventId, int eventName, double componentId, int value);
	
	void PublishDbData(double eventId1, double eventId, string eventName, int componentId, double value);
    }
}
