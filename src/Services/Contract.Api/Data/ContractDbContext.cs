using Microsoft.EntityFrameworkCore;

namespace Contract.Api.Data;

public class ContractDbContext(DbContextOptions<ContractDbContext> options) : DbContext(options)
{
    // Các DbSet (bảng) sẽ được thêm ở các bước sau.
}
