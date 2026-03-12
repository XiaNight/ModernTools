using System;
using System.Diagnostics;
using System.Reflection;

[AttributeUsage(AttributeTargets.Method)]
public sealed class MeasureTimeAttribute : Attribute { }

public class TimingProxy<T> : DispatchProxy
{
    public T Target;

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        var hasAttr = targetMethod.GetCustomAttribute<MeasureTimeAttribute>() != null;

        if (!hasAttr)
            return targetMethod.Invoke(Target, args);

        var sw = Stopwatch.StartNew();
        try
        {
            return targetMethod.Invoke(Target, args);
        }
        finally
        {
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"{targetMethod.DeclaringType.FullName}.{targetMethod.Name} took {sw.Elapsed.TotalMilliseconds:F3} ms");
        }
    }

    public static T Create(T target)
    {
        var proxy = DispatchProxy.Create<T, TimingProxy<T>>();
        ((TimingProxy<T>)(object)proxy).Target = target;
        return proxy;
    }
}