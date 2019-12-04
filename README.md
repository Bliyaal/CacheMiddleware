# CacheMiddleware

CacheMiddleware is a small piece of code to do the job of using IMemoryCache for you.

## Installation

Install with Nuget : [Package page](https://www.nuget.org/packages/bliyaal.cachemiddleware/)

## Usage

In your Startup.cs
```CS
app.UseCache();
```

In your controller at class or action level

```CS
[Cache(AbsoluteExpirationRelativeToNow = 10)]
public IActionResult SomeAction()
{
    ...
}
```

## License
[MIT](https://choosealicense.com/licenses/mit/)
