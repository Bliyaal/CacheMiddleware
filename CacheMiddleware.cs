using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace CacheMiddleware
{
    public class CacheMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;

        public CacheMiddleware(RequestDelegate next,
                               IMemoryCache cache)
        {
            _next = next;
            _cache = cache;
        }

        private async Task ExecuteRequest(HttpContext context, string key, CacheAttribute attribute)
        {
            if (!IsClearCacheAction(context, attribute))
            {
                await ExecuteRequestAndCache(context, key, attribute);
            }
            else
            {
                await _next(context);
            }
        }

        private async Task ExecuteRequestAndCache(HttpContext context, string key, CacheAttribute attribute)
        {
            var originalBodyStream = context.Response.Body;

            using (var responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;
                await _next(context);

                if (IsCachableResponse(context.Response))
                {
                    context.Response.Body.Seek(0, SeekOrigin.Begin);
                    var buffer = new byte[Convert.ToInt32(context.Response.Body.Length)];
                    context.Response.Body.Read(buffer, 0, buffer.Length);

                    _cache.Set(key, buffer, GetOptions(attribute));

                    context.Response.Body.Seek(0, SeekOrigin.Begin);

                    await responseBody.CopyToAsync(originalBodyStream);
                }
            }
        }

        private CacheAttribute GetCacheAttribute(RouteData routeData)
        {
            if (routeData != null)
            {
                var controller = GetControllerFromRouteDAta(routeData);
                var method = GetMethodFromRouteData(routeData, controller);

                var attribute = controller.GetCustomAttribute<CacheAttribute>();
                return method.GetCustomAttribute<CacheAttribute>() ?? attribute;
            }

            return null;
        }

        private Type GetControllerFromRouteDAta(RouteData routeData)
        {
            if (routeData != null)
            {
                var controllerName = routeData.Values.Where(v => v.Key.Equals("controller", StringComparison.CurrentCultureIgnoreCase))
                                                     .First()
                                                     .Value.ToString();

                return Assembly.GetEntryAssembly()
                               .GetTypes()
                               .First(t => t.Name.Equals($"{controllerName}Controller"));

            }
            return null;
        }

        private MethodInfo GetMethodFromRouteData(RouteData routeData, Type controller)
        {
            if (routeData != null && controller != null)
            {
                var methodName = routeData.Values.Where(v => v.Key.Equals("action", StringComparison.CurrentCultureIgnoreCase))
                                                 .First()
                                                 .Value.ToString();

                return controller.GetMethods()
                                 .First(m => m.IsPublic
                                        && m.Name.Equals(methodName, StringComparison.CurrentCultureIgnoreCase)
                                        && m.GetParameters().Count() == routeData.Values.Count() - 2);

            }
            return null;
        }

        private MemoryCacheEntryOptions GetOptions(CacheAttribute attribute)
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = attribute.AbsoluteExpiration > DateTime.MinValue ? attribute.AbsoluteExpiration : (DateTimeOffset?)null,
                AbsoluteExpirationRelativeToNow = attribute.AbsoluteExpirationRelativeToNow > int.MinValue ? TimeSpan.FromSeconds(attribute.AbsoluteExpirationRelativeToNow) : (TimeSpan?)null,
                Priority = attribute.Priority,
                SlidingExpiration = attribute.SlidingExpiration > int.MinValue ? TimeSpan.FromSeconds(attribute.SlidingExpiration) : (TimeSpan?)null,
                Size = attribute.Size > long.MinValue ? attribute.Size : (long?)null
            };
        }

        public async Task Invoke(HttpContext context)
        {
            var data = context.GetRouteData();

            if (data != null)
            {
                await InvokeRouted(context, data);
            }
            else
            {
                await _next(context);
            }
        }

        private async Task InvokeCached(HttpContext context, CacheAttribute attribute)
        {
            var key = context.Request.Path;

            if (IsClearCacheAction(context, attribute))
            {
                _cache.Remove(key);
            }

            _cache.TryGetValue(key, out object value);

            if (value == null)
            {
                await ExecuteRequest(context, key, attribute);
            }
            else
            {
                await SetResponseFromCache(context, (byte[])value);
            }
        }

        private async Task InvokeRouted(HttpContext context, RouteData data)
        {
            var attribute = GetCacheAttribute(data);

            if (attribute.CacheResult)
            {
                await InvokeCached(context, attribute);
            }
            else
            {
                await _next(context);
            }
        }

        private bool IsCachableResponse(HttpResponse response)
        {
            Enum.TryParse(response?.StatusCode.ToString(), out HttpStatusCode code);

            return code >= HttpStatusCode.OK
                   && code < HttpStatusCode.MultipleChoices
                   && code != HttpStatusCode.NoContent
                   && code != HttpStatusCode.ResetContent;
        }

        private bool IsClearCacheAction(HttpContext context, CacheAttribute cacheAttr)
        {
            return (new[] { "PATCH", "POST", "PUT" }.Any(method => method.Equals(context.Request.Method,
                                                                                 StringComparison.CurrentCultureIgnoreCase))
                    && !cacheAttr.AsGet)
                    || context.Request.Method.Equals("DELETE", StringComparison.CurrentCultureIgnoreCase);
        }

        private async Task SetResponseFromCache(HttpContext context, byte[] value)
        {
            var originalBodyStream = context.Response.Body;

            using (var responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;

                context.Response.Body.Seek(0, SeekOrigin.Begin);
                context.Response.Body.Write(value, 0, value.Length);
                context.Response.Body.Seek(0, SeekOrigin.Begin);

                await responseBody.CopyToAsync(originalBodyStream);
            }
        }

    }
}
