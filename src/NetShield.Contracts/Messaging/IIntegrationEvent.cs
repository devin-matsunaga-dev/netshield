namespace NetShield.Contracts.Messaging;

/// <summary>
/// Marks a payload that may travel between modules on the in-process bus.
/// <para>
/// ARCHITECTURE.md §5: cross-module communication is one-way, asynchronous, and carries
/// <c>NetShield.Contracts</c> types only. Implementing this interface is what makes a type
/// eligible for the outbox; an EF entity never does, because entities do not cross a module
/// boundary (ARCHITECTURE.md §4).
/// </para>
/// </summary>
public interface IIntegrationEvent;
