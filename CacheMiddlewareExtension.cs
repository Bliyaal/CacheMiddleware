using Microsoft.AspNetCore.Builder;

namespace Bliyaal.CacheMiddleware
{
    public static class CacheMiddlewareExtension
    {
        public static IApplicationBuilder UseCache(this IApplicationBuilder app)
        {
            return app.UseRouting()
                      .UseMiddleware<CacheMiddleware>();
        }
    }
}