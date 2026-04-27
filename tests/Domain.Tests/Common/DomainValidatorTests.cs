// T012 (TDD red) — written before T014 (DomainValidator lands).
// MUST fail to build until SddDemo.Ledger.Domain.Common.DomainValidator exists.

using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using SddDemo.Ledger.Domain.Common;
using Xunit;

namespace SddDemo.Ledger.Domain.Tests.Common;

public class DomainValidatorTests
{
    private sealed record SampleDto(
        [property: Required, StringLength(10, MinimumLength = 1)] string Name,
        [property: Range(1, 100)] int Quantity);

    [Fact]
    public void Validate_returns_success_for_valid_input()
    {
        var dto = new SampleDto("ok", 5);

        var result = DomainValidator.Validate(dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(dto);
    }

    [Fact]
    public void Validate_returns_validation_failure_for_missing_required()
    {
        var dto = new SampleDto(string.Empty, 5);

        var result = DomainValidator.Validate(dto);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error!.Code.Should().Be("validation");
    }

    [Fact]
    public void Validate_aggregates_multiple_violations_into_one_error()
    {
        var dto = new SampleDto(new string('x', 50), 999);

        var result = DomainValidator.Validate(dto);

        result.IsFailure.Should().BeTrue();
        result.Error!.Message.Should().Contain(nameof(SampleDto.Name));
        result.Error!.Message.Should().Contain(nameof(SampleDto.Quantity));
    }

    private sealed record CrossPropertyDto(int Min, int Max) : IValidatableObject
    {
        public IEnumerable<ValidationResult> Validate(ValidationContext _)
        {
            if (Min > Max)
            {
                yield return new ValidationResult(
                    "Min must be <= Max.",
                    [nameof(Min), nameof(Max)]);
            }
        }
    }

    [Fact]
    public void Validate_runs_IValidatableObject_cross_property_rules()
    {
        var dto = new CrossPropertyDto(10, 5);

        var result = DomainValidator.Validate(dto);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error!.Message.Should().Contain("Min must be <= Max");
    }
}
