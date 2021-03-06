﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Bliyaal.CacheMiddleware
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

                    var options = GetOptions(attribute);
                    _cache.Set(key, buffer, options);
                    _cache.Set(GetContentTypeKey(key), context.Response.ContentType, options);

                    context.Response.Body.Seek(0, SeekOrigin.Begin);

                    await responseBody.CopyToAsync(originalBodyStream);
                }
            }
        }

        private CacheAttribute GetCacheAttribute(RouteData routeData)
        {
            if (routeData != null)
            {
                var controller = GetControllerFromRouteData(routeData);
                var method = GetMethodFromRouteData(routeData, controller);

                var attribute = controller.GetCustomAttribute<CacheAttribute>();
                return method.GetCustomAttribute<CacheAttribute>() ?? attribute;
            }

            return new CacheAttribute { CacheResult = false };
        }

        private string GetContentTypeKey(string key)
        {
            return $"{key}ContentType";
        }

        private Type GetControllerFromRouteData(RouteData routeData)
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

        private string GetKey(HttpContext context)
        {
            var path = Encoding.UTF8.GetBytes(context.Request.Path);
            var method = Encoding.UTF8.GetBytes(context.Request.Method);
            var body = GetRequestBodyData(context);
            var uniqueData = new byte[path.Length + method.Length + (body?.Length ?? 0)];

            path.CopyTo(uniqueData, 0);
            method.CopyTo(uniqueData, path.Length);
            body?.CopyTo(uniqueData, path.Length + method.Length);

            var hash = GetHash(uniqueData);

            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("X2"));
            }

            return sb.ToString();
        }

        private byte[] GetHash(byte[] uniqueData)
        {
            var sha256 = SHA256.Create();
            return sha256.ComputeHash(uniqueData);
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
                                        && m.Name.Equals(methodName, StringComparison.CurrentCultureIgnoreCase));
            }
            return null;
        }

        private MemoryCacheEntryOptions GetOptions(CacheAttribute attribute)
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = attribute.AbsoluteExpiration > DateTime.MinValue
                                     ? attribute.AbsoluteExpiration
                                     : (DateTimeOffset?)null,

                AbsoluteExpirationRelativeToNow = attribute.AbsoluteExpirationRelativeToNow > int.MinValue
                                                  ? TimeSpan.FromSeconds(attribute.AbsoluteExpirationRelativeToNow)
                                                  : (TimeSpan?)null,

                Priority = attribute.Priority,

                SlidingExpiration = attribute.SlidingExpiration > int.MinValue
                                    ? TimeSpan.FromSeconds(attribute.SlidingExpiration)
                                    : (TimeSpan?)null,

                Size = attribute.Size > long.MinValue
                       ? attribute.Size
                       : (long?)null
            };
        }

        private byte[] GetRequestBodyData(HttpContext context)
        {
            if (context.Request.ContentLength.HasValue)
            {
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;

                var buffer = new byte[(int)context.Request.ContentLength];
                context.Request.Body.Read(buffer, 0, (int)context.Request.ContentLength);

                context.Request.Body.Position = 0;

                return buffer;
            }
            return null;
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
            var key = GetKey(context);

            if (IsClearCacheAction(context, attribute))
            {
                _cache.Remove(key);
                _cache.Remove(GetContentTypeKey(key));
            }

            _cache.TryGetValue(key, out object value);

            if (value == null)
            {
                await ExecuteRequest(context, key, attribute);
            }
            else
            {
                _cache.TryGetValue(GetContentTypeKey(key), out string contentType);
                await SetResponseFromCache(context, (byte[])value, contentType);
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

        private async Task SetResponseFromCache(HttpContext context, byte[] value, string contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                context.Response.OnStarting(state =>
                {
                    var httpContext = (HttpContext)state;
                    httpContext.Response.Headers.Add("Content-Type", contentType);
                    return Task.CompletedTask;
                }, context);
            }

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