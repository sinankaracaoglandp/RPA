namespace RPA.Infrastructure.SAP;

using System.Globalization;
using System.Reflection;

internal static class SapCom
{
    public static object? Get(object target, string name)
        => target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);

    public static object? Get(object target, string name, params object?[] args)
    {
        try
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty,
                binder: null,
                target,
                args,
                CultureInfo.InvariantCulture);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    public static void Set(object target, string name, object? value)
        => target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty,
            binder: null,
            target,
            args: new[] { value },
            CultureInfo.InvariantCulture);

    public static object? Invoke(object target, string name, params object?[] args)
    {
        try
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.InvokeMethod,
                binder: null,
                target,
                args,
                CultureInfo.InvariantCulture);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
