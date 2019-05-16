using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Internal;

namespace CacheMiddleware
{
    public static class CacheMiddlewareExtension
    {
        public static IApplicationBuilder UseCache(this IApplicationBuilder app)
        {
            return app.UseEndpointRouting()
                      .UseMiddleware<CacheMiddleware>();
        }
    }
}
