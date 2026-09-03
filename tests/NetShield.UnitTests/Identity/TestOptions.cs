using Microsoft.Extensions.Options;

namespace NetShield.UnitTests.Identity;

/// <summary>Wraps a configured options instance for a constructor that wants one.</summary>
internal static class TestOptions
{
    internal static IOptions<T> For<T>(T value) where T : class => Options.Create(value);
}
