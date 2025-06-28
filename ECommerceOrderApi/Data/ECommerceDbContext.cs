using ECommerceOrderApi.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ECommerceOrderApi.Data;

public class ECommerceDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{

    public ECommerceDbContext(DbContextOptions<ECommerceDbContext> options): base(options)
    {
    }

    public DbSet<Order> Orders { get; set; }
    public DbSet<Product> Products { get; set; }
}

