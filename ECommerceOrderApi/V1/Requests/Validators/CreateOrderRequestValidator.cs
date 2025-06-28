using FluentValidation;
using FluentValidation.Results;

namespace ECommerceOrderApi.V1.Requests.Validators;

public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    protected override bool PreValidate(ValidationContext<CreateOrderRequest> context, ValidationResult result)
    {
        if (context.InstanceToValidate is null)
        {
            result.Errors.Add(new ValidationFailure("CreateOrderRequest", "Please ensure a CreateOrderRequest was supplied."));
            return false;
        }
        return true;
    }

    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.Items)
            .NotNull().WithMessage("En az bir ürün seçilmelidir")
            .Must(items => items is not null && items.Count > 0)
            .WithMessage("En az bir ürün seçilmelidir");

        RuleForEach(x => x.Items)
            .SetValidator(new OrderItemRequestValidator());   
    }
}

public class OrderItemRequestValidator : AbstractValidator<OrderItemRequest>
{
    public OrderItemRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty()
            .GreaterThan(0);

        RuleFor(x => x.Quantity)
            .NotEmpty()
            .GreaterThan(0);
    }
}
