using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Repository;

public partial class ApplicationDbContext : DbContext
{
	partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<BuyActionLog>()
			.Property<bool>(LongTimeframeSignalConstants.IsLongTimeframeProperty)
			.HasDefaultValue(false);

		modelBuilder.Entity<SellActionLog>()
			.Property<bool>(LongTimeframeSignalConstants.IsLongTimeframeProperty)
			.HasDefaultValue(false);
	}
}
