using Microsoft.Extensions.Caching.Memory;
using System;

namespace Bliyaal.CacheMiddleware
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class CacheAttribute : Attribute
    {
        //
        // Summary:
        //     Gets or sets if the result is cached. The default is true.
        public bool CacheResult { get; set; } = true;
        //
        // Summary:
        //     Gets or sets if the method is considered a GET. The default is false.
        public bool AsGet { get; set; } = false;
        //
        // Summary:
        //     Gets or sets an absolute expiration date for the cache entry.
        public DateTime AbsoluteExpiration { get; set; } = DateTime.MinValue;
        //
        // Summary:
        //     Gets or sets an absolute expiration time, relative to now, in seconds.
        public int AbsoluteExpirationRelativeToNow { get; set; } = int.MinValue;
        //
        // Summary:
        //     Gets or sets how long, in seconds, a cache entry can be inactive (e.g. not accessed) before
        //     it will be removed. This will not extend the entry lifetime beyond the absolute
        //     expiration (if set).
        public int SlidingExpiration { get; set; } = int.MinValue;
        //
        // Summary:
        //     Gets or sets the priority for keeping the cache entry in the cache during a memory
        //     pressure triggered cleanup. The default is Microsoft.Extensions.Caching.Memory.CacheItemPriority.Normal.
        public CacheItemPriority Priority { get; set; } = CacheItemPriority.Normal;
        //
        // Summary:
        //     Gets or sets the size of the cache entry value.
        public long Size { get; set; } = long.MinValue;
    }
}
