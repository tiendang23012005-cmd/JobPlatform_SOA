using Microsoft.EntityFrameworkCore;

namespace Payment.Api.Data;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    // Các DbSet (bảng) sẽ được thêm ở các bước sau.
}
