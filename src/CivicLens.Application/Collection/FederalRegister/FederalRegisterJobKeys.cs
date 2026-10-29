namespace CivicLens.Application.Collection.FederalRegister;

public static class FederalRegisterJobKeys
{
    public static string Document(string sourceId, string documentNumber) => $"federal-register/{sourceId}/document/{documentNumber}";
}
