using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace JitRealm.Mud;

/// <summary>
/// Centralized callout invocation with per-type method caching.
/// Optimizes the common cases (no args / ctx-only) by compiling delegates.
/// Falls back to reflection for uncommon signatures.
/// </summary>
public static class CallOutInvoker
{
    private readonly record struct CacheKey(RuntimeTypeHandle TypeHandle, string MethodName);

    private sealed class Cached
    {
        public required MethodInfo Method { get; init; }
        public required bool HasContextFirst { get; init; }
        public required int ParamCount { get; init; }

        // Fast paths for common signatures
        public Action<object>? NoArgsInvoker { get; init; }
        public Action<object, IMudContext>? CtxOnlyInvoker { get; init; }
    }

    private static readonly ConcurrentDictionary<CacheKey, Cached?> Cache = new();

    public static bool TryInvoke(IMudObject target, CallOutEntry callout, MudContext ctx, Action<string>? logError = null)
    {
        var cached = GetCached(target.GetType(), callout.MethodName, logError);
        if (cached is null)
            return false;

        // Fast paths
        if (cached.ParamCount == 0 && cached.NoArgsInvoker is not null)
        {
            cached.NoArgsInvoker(target);
            return true;
        }

        if (cached.ParamCount == 1 && cached.HasContextFirst && cached.CtxOnlyInvoker is not null)
        {
            cached.CtxOnlyInvoker(target, ctx);
            return true;
        }

        // General path: build exact argument array and invoke.
        var args = BuildInvokeArgs(cached, ctx, callout.Args);
        cached.Method.Invoke(target, args);
        return true;
    }

    private static object?[] BuildInvokeArgs(Cached cached, IMudContext ctx, object?[]? calloutArgs)
    {
        if (cached.ParamCount == 0)
            return Array.Empty<object?>();

        var invokeArgs = new object?[cached.ParamCount];

        if (cached.HasContextFirst)
        {
            invokeArgs[0] = ctx;
            if (calloutArgs is not null)
            {
                for (int i = 1; i < invokeArgs.Length && i - 1 < calloutArgs.Length; i++)
                    invokeArgs[i] = calloutArgs[i - 1];
            }
        }
        else if (calloutArgs is not null)
        {
            for (int i = 0; i < invokeArgs.Length && i < calloutArgs.Length; i++)
                invokeArgs[i] = calloutArgs[i];
        }

        return invokeArgs;
    }

    private static Cached? GetCached(Type type, string methodName, Action<string>? logError)
    {
        var key = new CacheKey(type.TypeHandle, methodName);
        return Cache.GetOrAdd(key, _ =>
        {
            var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
            {
                logError?.Invoke($"Method '{methodName}' not found on {type.FullName}");
                return null;
            }

            var parameters = method.GetParameters();
            var hasCtx = parameters.Length > 0 && parameters[0].ParameterType == typeof(IMudContext);

            Action<object>? noArgs = null;
            Action<object, IMudContext>? ctxOnly = null;

            // Compile fast delegates for common signatures.
            if (parameters.Length == 0 && method.ReturnType == typeof(void))
            {
                noArgs = CompileNoArgsInvoker(type, method);
            }
            else if (parameters.Length == 1 && hasCtx && method.ReturnType == typeof(void))
            {
                ctxOnly = CompileCtxOnlyInvoker(type, method);
            }

            return new Cached
            {
                Method = method,
                HasContextFirst = hasCtx,
                ParamCount = parameters.Length,
                NoArgsInvoker = noArgs,
                CtxOnlyInvoker = ctxOnly
            };
        });
    }

    private static Action<object> CompileNoArgsInvoker(Type declaringType, MethodInfo method)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var cast = Expression.Convert(instance, declaringType);
        var call = Expression.Call(cast, method);
        return Expression.Lambda<Action<object>>(call, instance).Compile();
    }

    private static Action<object, IMudContext> CompileCtxOnlyInvoker(Type declaringType, MethodInfo method)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var ctx = Expression.Parameter(typeof(IMudContext), "ctx");
        var cast = Expression.Convert(instance, declaringType);
        var call = Expression.Call(cast, method, ctx);
        return Expression.Lambda<Action<object, IMudContext>>(call, instance, ctx).Compile();
    }
}


