namespace RPA.Infrastructure.Workflow;

using RPA.Domain.Interfaces;

/// <summary>
/// Activity ID'den çalıştırılabilir <see cref="IActivity"/> örneği üretir.
/// BaseRunner, <c>type=activity</c> node'larını çalıştırmak için bunu kullanır.
/// Gerçek aktivite implementasyonları Faz 2.6–2.9'da eklenir; o zamana dek
/// <see cref="EmptyActivityFactory"/> (null döndürür) kaydedilir.
/// </summary>
public interface IActivityFactory
{
    /// <summary>Verilen ID için yeni aktivite örneği; bilinmiyorsa null.</summary>
    IActivity? CreateActivity(string activityId);
}

/// <summary>Hiçbir aktivite implementasyonu kayıtlı değilken kullanılan varsayılan factory.</summary>
public sealed class EmptyActivityFactory : IActivityFactory
{
    public IActivity? CreateActivity(string activityId) => null;
}

/// <summary>Delegate tabanlı basit factory (test ve dinamik kayıt için).</summary>
public sealed class DelegateActivityFactory : IActivityFactory
{
    private readonly Func<string, IActivity?> _resolver;

    public DelegateActivityFactory(Func<string, IActivity?> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public IActivity? CreateActivity(string activityId) => _resolver(activityId);
}
