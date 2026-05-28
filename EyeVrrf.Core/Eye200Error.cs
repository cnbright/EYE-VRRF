namespace EyeVrrf.Core;

public sealed class Eye200Error : Exception
{
    public Eye200Error(string message)
        : base(message)
    {
    }

    public Eye200Error(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
