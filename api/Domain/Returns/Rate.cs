namespace Finance.Api.Domain.Returns;

/// <summary>A return or a rate as a fraction: <c>0.1234</c> is 12.34%. Not a percentage.</summary>
public readonly record struct Rate(decimal Value);
