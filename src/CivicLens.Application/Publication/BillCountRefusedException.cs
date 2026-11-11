namespace CivicLens.Application.Publication;

/// <summary>A bill count snapshot or its source rows fail an expected check. The owner commands report it for the release. Other operational errors are not this type.</summary>
public sealed class BillCountRefusedException : InvalidOperationException
{
    public BillCountRefusedException(string message) : base(message) { }

    public BillCountRefusedException(string message, Exception innerException) : base(message, innerException) { }
}
