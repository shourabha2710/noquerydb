using Microsoft.EntityFrameworkCore;
using NoQueryDatabase.Model.Login;
using NoQueryDB.Api.DatabaseContext;

namespace NoQueryDB.Api.Service
{
    public interface IDatasourceRepository
    {
        Task<Datasource?> GetByIdAsync(int id, int companyId);
    }
    public class DatasourceRepository : IDatasourceRepository
    {
        private readonly AppDbContext _db;

        public DatasourceRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<Datasource?> GetByIdAsync(int id, int companyId)
        {
            return await _db.Datasources
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.CompanyId == companyId &&
                    x.DeletedAt == null);
        }
    }
}
