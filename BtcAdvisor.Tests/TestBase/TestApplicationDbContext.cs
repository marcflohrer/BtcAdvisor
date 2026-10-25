using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository;

namespace BtcAdvisor.Tests
{
    public class TestApplicationDbContext (string connectionString) : ApplicationDbContext (new DbContextOptions<ApplicationDbContext> ())
    {
        private readonly string _connectionString = connectionString;

        protected override void OnConfiguring (DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql (_connectionString);
            }
        }
    }
}
