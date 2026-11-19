using Microsoft.EntityFrameworkCore;

namespace Job.Api.Data;

public class JobDbContext(DbContextOptions<JobDbContext> options) : DbContext(options)
{
    // Các DbSet (bảng) sẽ được thêm ở các bước sau.
}
