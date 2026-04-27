// T011 (TDD red) — written before T013 (Result/Error/ErrorType land).
// MUST fail to build until SddDemo.Ledger.Domain.Common types exist.

using FluentAssertions;
using SddDemo.Ledger.Domain.Common;
using Xunit;

namespace SddDemo.Ledger.Domain.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_carries_value_and_no_error()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_carries_error_and_no_value()
    {
        var error = new Error("test.failure", "boom", ErrorType.Failure);

        var result = Result<int>.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void NonGeneric_Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void NonGeneric_Failure_carries_error()
    {
        var error = new Error("test.failure", "boom", ErrorType.Failure);

        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Map_transforms_success_value()
    {
        var result = Result<int>.Success(2).Map(x => x * 21);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Map_short_circuits_on_failure()
    {
        var error = new Error("e", "m", ErrorType.Validation);

        var result = Result<int>.Failure(error).Map(x => x * 21);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Bind_chains_successful_results()
    {
        var result = Result<int>.Success(2).Bind(x => Result<string>.Success($"#{x}"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("#2");
    }

    [Fact]
    public void Bind_short_circuits_on_failure()
    {
        var error = new Error("e", "m", ErrorType.Validation);

        var result = Result<int>.Failure(error).Bind(x => Result<string>.Success($"#{x}"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Result_T_with_same_value_and_error_are_equal()
    {
        var a = Result<int>.Success(1);
        var b = Result<int>.Success(1);

        a.Should().Be(b);
    }

    [Fact]
    public void Error_record_value_equality()
    {
        var a = new Error("c", "m", ErrorType.NotFound);
        var b = new Error("c", "m", ErrorType.NotFound);

        a.Should().Be(b);
    }
}
