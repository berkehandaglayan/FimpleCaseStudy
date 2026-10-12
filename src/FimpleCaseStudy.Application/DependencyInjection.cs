using Microsoft.Extensions.DependencyInjection;
using FimpleCaseStudy.Application.Atms;

namespace FimpleCaseStudy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AtmService>();
        return services;
    }
}
