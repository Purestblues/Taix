using System;

namespace ShiJian.Client;

public static class ServiceLocator
{
    private static IServiceProvider? _serviceProvider;

    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public static T? GetService<T>() where T : class
    {
        return _serviceProvider?.GetService(typeof(T)) as T;
    }

    public static T GetRequiredService<T>() where T : class
    {
        if (_serviceProvider == null) throw new InvalidOperationException("Service provider has not been initialized.");

        var service = _serviceProvider.GetService(typeof(T)) as T;
        if (service == null) throw new InvalidOperationException($"Service of type {typeof(T)} not found.");
        return service;
    }

    public static object GetRequiredService(Type type)
    {
        if (_serviceProvider == null) throw new InvalidOperationException("Service provider has not been initialized.");
        var service = _serviceProvider.GetService(type);
        if (service == null) throw new InvalidOperationException($"Service of type {type} not found.");
        return service;
    }
}
