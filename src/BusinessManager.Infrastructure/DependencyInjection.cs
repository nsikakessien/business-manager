using BusinessManager.Application.Customers;
using BusinessManager.Application.Products;
using BusinessManager.Application.Reports;
using BusinessManager.Application.Sales;
using BusinessManager.Infrastructure.Persistence;
using BusinessManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessManagerInfrastructure(
        this IServiceCollection services,
        DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);

        services.AddDbContextFactory<BusinessManagerDbContext>(builder =>
            builder.UseSqlite(options.ConnectionString));

        services.AddSingleton<DatabaseBackupService>();
        services.AddSingleton<DatabaseInitializer>();

        // Services are stateless; each call uses its own short-lived context.
        services.AddSingleton<IProductService, ProductService>();
        services.AddSingleton<ICustomerService, CustomerService>();
        services.AddSingleton<ISalesService, SalesService>();
        services.AddSingleton<IDashboardService, DashboardService>();

        return services;
    }
}
