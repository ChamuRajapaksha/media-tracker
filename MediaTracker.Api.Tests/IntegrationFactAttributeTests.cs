namespace MediaTracker.Api.Tests;

public sealed class IntegrationFactAttributeTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("yes", false)]
    [InlineData(" 1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_value_one_counts_as_an_opt_in(string? value, bool expected)
    {
        Assert.Equal(expected, IntegrationFactAttribute.IsOptedIn(value));
    }

    [Fact]
    public void An_unset_opt_in_variable_skips_the_test_and_says_how_to_enable_it()
    {
        using var scope = EnvironmentScope.Unset(IntegrationFactAttribute.OptInVariable);

        var skip = new IntegrationFactAttribute().Skip;

        Assert.NotNull(skip);
        Assert.Contains(IntegrationFactAttribute.OptInVariable, skip);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("")]
    public void A_value_other_than_one_also_skips_the_test(string value)
    {
        using var scope = EnvironmentScope.Set(IntegrationFactAttribute.OptInVariable, value);

        Assert.NotNull(new IntegrationFactAttribute().Skip);
    }

    [Fact]
    public void The_opt_in_variable_set_to_one_leaves_the_test_runnable()
    {
        using var scope = EnvironmentScope.Set(IntegrationFactAttribute.OptInVariable, "1");

        Assert.Null(new IntegrationFactAttribute().Skip);
    }

    [Fact]
    public void The_restore_scope_puts_the_original_value_back()
    {
        var original = Environment.GetEnvironmentVariable(IntegrationFactAttribute.OptInVariable);

        using (EnvironmentScope.Set(IntegrationFactAttribute.OptInVariable, "1"))
        {
            Assert.Equal("1", Environment.GetEnvironmentVariable(IntegrationFactAttribute.OptInVariable));
        }

        Assert.Equal(original, Environment.GetEnvironmentVariable(IntegrationFactAttribute.OptInVariable));
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        private EnvironmentScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static EnvironmentScope Unset(string name) => new(name, null);

        public static EnvironmentScope Set(string name, string value) => new(name, value);

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
