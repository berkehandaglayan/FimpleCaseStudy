using Microsoft.Extensions.DependencyInjection;

namespace FimpleCaseStudy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
