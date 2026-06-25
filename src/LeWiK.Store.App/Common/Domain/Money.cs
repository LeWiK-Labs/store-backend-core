namespace LeWiK.Store.App.Common.Domain;

public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3) throw new ArgumentException("Currency debe ser ISO 4217 (3 letras)", nameof(currency));
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public Money Add(Money other) { EnsureSame(other); return new(Amount + other.Amount, Currency); }
    public Money Substract(Money other) { EnsureSame(other); return new(Amount - other.Amount, Currency); }
    public Money Multiply(int qty) => new(Amount * qty, Currency);
    
    private void EnsureSame(Money other)
    {
        if(Currency != other.Currency)
            throw new InvalidOperationException($"La moneda {Currency} difiera de la moneda {other.Currency}.");
    }
    
    public override string ToString() => $"{Amount:0.0000} {Currency}";
}