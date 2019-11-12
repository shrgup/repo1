using System;

public class BackupPlanDatabase
{
    [XmlAttribute("Name")]
    public String DatabaseName { set; get; }

    [XmlText]
    public String SqlInstance { set; get; }

    [XmlIgnore]
    public DatabaseCategory Category { set; get; }

    ISqlConnectionInfo m_connectionInfo;

    //This is set to true when the user selects this database for restore
    [XmlIgnore]
    public bool DatabaseSelected { set; get; }

    [XmlIgnore]
    public ISqlConnectionInfo ConnectionInfo
    {
        set
        {
            m_connectionInfo = value;


            SqlInstance = m_connectionInfo.DataSource;
            DatabaseName = m_connectionInfo.InitialCatalog;
        }
        get
        {
            if (m_connectionInfo != null)
            {
                return m_connectionInfo;
            }
            else if (!String.IsNullOrEmpty(SqlInstance) && !String.IsNullOrEmpty(SqlInstance))
            {
                SqlConnectionStringBuilder sb = new SqlConnectionStringBuilder()
                {
                    DataSource = SqlInstance,
                    InitialCatalog = DatabaseName,
                    IntegratedSecurity = true,
                };
                return SqlConnectionInfoFactory.Create(sb.ConnectionString);
            }
            else
            {
                return null;
            }
        }
    }   

    public BackupPlanDatabase()
    {
    }

    public override bool Equals(object obj)
    {
        BackupPlanDatabase right = obj as BackupPlanDatabase;
        return right != null &&
               Category == right.Category &&
               String.Equals(DatabaseName, right.DatabaseName, StringComparison.OrdinalIgnoreCase) &&
               String.Equals(SqlInstance, right.SqlInstance, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
}