using graphqlodata.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RepositoryServices
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            services.AddScoped<IBooksRepository, BooksRepository>();
            services.AddScoped<ICustomerRepository, CustomerRepository>();
            return services;
        }
    }
}
