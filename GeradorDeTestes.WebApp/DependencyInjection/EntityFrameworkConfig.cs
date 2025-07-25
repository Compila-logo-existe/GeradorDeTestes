using GeradorDeTestes.Infraestrutura.ORM.Compartilhado;
using Microsoft.EntityFrameworkCore;

namespace GeradorDeTestes.WebApp.DependencyInjection;

public static class EntityFrameworkConfig
{
    public static void AddEntityFrameworkConfig(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<GeradorDeTestesDbContext>(options =>
        options.UseNpgsql(configuration["SQL_CONNECTION_STRING"]));
    }
}
