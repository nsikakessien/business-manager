using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BusinessManager.Infrastructure.Persistence;

public sealed class BusinessManagerDbContextFactory
    : IDesignTimeDbContextFactory<BusinessManagerDbContext>
{
    public BusinessManagerDbContext CreateDbContext(string[] args)
    {
        var options =
            new DbContextOptionsBuilder<BusinessManagerDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;

        return new BusinessManagerDbContext(options);
    }
}