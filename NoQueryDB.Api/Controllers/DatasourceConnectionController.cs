using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Service;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/datasources")]
    [Authorize]
    public class DatasourceConnectionController : ControllerBase
    {
        private readonly IDatasourceRepository _repo;
        private readonly IActiveDatasourceService _activeDs;
        private readonly ISecretProtector _crypto;

        public DatasourceConnectionController(
            IDatasourceRepository repo,
            IActiveDatasourceService activeDs,
            ISecretProtector crypto)
        {
            _repo = repo;
            _activeDs = activeDs;
            _crypto = crypto;
        }

        [HttpPost("{id:int}/connect")]
        public async Task<IActionResult> Connect(int id)
        {
            var userId = User.GetUserId();
            var companyId = User.GetCompanyId();

            var ds = await _repo.GetByIdAsync(id, companyId);
            if (ds == null)
                return NotFound(new { message = "Datasource not found" });

            // 🔐 decrypt password only in memory
            var password = ds.UseWindowsAuth
                ? null
                : _crypto.Decrypt(ds.EncryptedPassword!);

            // 🔐 validate connection
            using var conn = SqlConnectionFactory.Create(ds, password);
            await conn.OpenAsync(); // throws if invalid

            _activeDs.SetActive(userId, id);

            return Ok(new
            {
                success = true,
                datasourceId = id,
                message = "Datasource connected"
            });
        }

        [HttpPost("disconnect")]
        public IActionResult Disconnect()
        {
            var userId = User.GetUserId();

            _activeDs.Clear(userId);

            return Ok(new
            {
                success = true,
                message = "Datasource disconnected"
            });
        }

        [HttpGet("active")]
        public IActionResult Active()
        {
            var userId = User.GetUserId();
            var dsId = _activeDs.GetActive(userId);

            return Ok(new
            {
                connected = dsId.HasValue,
                datasourceId = dsId
            });
        }
    }

}
