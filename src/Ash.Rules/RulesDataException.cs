namespace Ash.Rules;

public sealed class RulesDataException : Exception
{
    public RulesDataException(string message)
        : base(message)
    {
    }

    public RulesDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RulesResolutionException : Exception
{
    public RulesResolutionException(string message)
        : base(message)
    {
    }
}

