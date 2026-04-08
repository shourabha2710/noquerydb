using NoQueryDatabase.Model.Login;
using System.Data.SqlClient;

namespace NoQueryDB.Api.Service
{
    public static class SqlConnectionFactory
    {
        public static SqlConnection Create(Datasource ds, string? password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = ds.Server,
                InitialCatalog = ds.DatabaseName,
                IntegratedSecurity = ds.UseWindowsAuth,
                TrustServerCertificate = true
            };

            if (!ds.UseWindowsAuth)
            {
                builder.UserID = ds.Username;
                builder.Password = password!;
            }

            return new SqlConnection(builder.ConnectionString);
        }
    }
}
