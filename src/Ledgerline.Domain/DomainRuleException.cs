namespace Ledgerline.Domain;

// Thrown when code tries to build something that breaks a domain rule, such
// as an unbalanced journal. Rule says which one, so a test or a caller can
// tell the rules apart without reading the message.
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(DomainRule rule, string message)
        : base(message)
    {
        Rule = rule;
    }

    public DomainRule Rule { get; }
}
