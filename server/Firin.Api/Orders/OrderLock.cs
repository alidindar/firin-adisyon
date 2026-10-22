namespace Firin.Api.Orders;

/// <summary>
/// Adisyon, kalem ve ödeme üzerindeki her değişiklik bu kilidi tutmalı (tek süreçli uygulama, düşük trafik).
/// PaymentService de bu kilidi kullanır.
/// </summary>
public sealed class OrderLock
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
