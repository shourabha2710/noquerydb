using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoQueryDB.Api.DatabaseContext;
using NoQueryDB.Api.Extensions;
using NoQueryDB.Api.Service;

namespace NoQueryDB.Api.Controllers
{
    [ApiController]
    [Route("api/functions")]
    [Authorize]
    public class FunctionsExplorerController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IActiveDatasourceService _activeDs;
        private readonly ISecretProtector _protector;
        private readonly ILogger<FunctionsExplorerController> _logger;

        public FunctionsExplorerController(
            AppDbContext db,
            IActiveDatasourceService activeDs,
            ISecretProtector protector,
            ILogger<FunctionsExplorerController> logger)
        {
            _db = db;
            _activeDs = activeDs;
            _protector = protector;
            _logger = logger;
        }

        
    }
}
