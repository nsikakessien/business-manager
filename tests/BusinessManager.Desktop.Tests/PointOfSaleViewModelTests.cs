using BusinessManager.Application.Common;
using BusinessManager.Application.Customers;
using BusinessManager.Application.Products;
using BusinessManager.Application.Sales;
using BusinessManager.Desktop.Services;
using BusinessManager.Desktop.ViewModels;
using BusinessManager.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace BusinessManager.Desktop.Tests;

public sealed class PointOfSaleViewModelTests
{
    private static readonly ProductListItem Water = new(Guid.NewGuid(), "WATER", "Water", 300m, 10, 0, true);
    private static readonly ProductListItem Empty = new(Guid.NewGuid(), "EMPTY", "Empty", 50m, 0, 0, true);
    private static readonly CustomerListItem Ada = new(Guid.NewGuid(), "Ada", null, null, null, 1000m, 200m, true);

    private static PointOfSaleViewModel Create() =>
        new(new Unused(), new Unused(), new Unused(), new NoDialogs(), NullLogger<PointOfSaleViewModel>.Instance);

    [Fact]
    public void EmptyCart_CannotBeCompleted()
    {
        var vm = Create();

        Assert.NotNull(vm.ValidationMessage);
        Assert.False(vm.CompleteSaleCommand.CanExecute(null));
    }

    [Fact]
    public void AddingSameProductTwice_IncrementsQuantity()
    {
        var vm = Create();

        vm.AddProductCommand.Execute(Water);
        vm.AddProductCommand.Execute(Water);

        var line = Assert.Single(vm.Cart);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(600m, vm.Total);
        Assert.Null(vm.ValidationMessage);
        Assert.True(vm.CompleteSaleCommand.CanExecute(null));
    }

    [Fact]
    public void OutOfStockProduct_IsNotAdded()
    {
        var vm = Create();

        vm.AddProductCommand.Execute(Empty);

        Assert.Empty(vm.Cart);
        Assert.Contains("out of stock", vm.ErrorMessage);
    }

    [Fact]
    public void QuantityAboveStock_OrInvalid_BlocksCompletion()
    {
        var vm = Create();
        vm.AddProductCommand.Execute(Water);

        vm.Cart[0].QuantityText = "11";
        Assert.Contains("Only 10", vm.ValidationMessage);

        vm.Cart[0].QuantityText = "abc";
        Assert.Contains("Check the quantity", vm.ValidationMessage);
        Assert.Equal(0m, vm.Total);

        vm.Cart[0].QuantityText = "3";
        Assert.Null(vm.ValidationMessage);
        Assert.Equal(900m, vm.Total);
    }

    [Fact]
    public void CashOverpayment_GivesChange_CardOverpaymentIsRejected()
    {
        var vm = Create();
        vm.AddProductCommand.Execute(Water);

        vm.AmountReceivedText = "500";
        Assert.Equal(300m, vm.AmountPaid);
        Assert.Equal(200m, vm.Change);
        Assert.Null(vm.ValidationMessage);

        vm.PaymentMethod = PaymentMethod.Card;
        Assert.Contains("Only cash", vm.ValidationMessage);
    }

    [Fact]
    public void Underpayment_RequiresCustomerWithEnoughCredit()
    {
        var vm = Create();
        vm.AddProductCommand.Execute(Water);
        vm.Cart[0].QuantityText = "4"; // 1,200

        vm.AmountReceivedText = "0";
        Assert.Equal(1200m, vm.OnAccount);
        Assert.Contains("Select a customer", vm.ValidationMessage);

        vm.SelectedCustomer = Ada; // 800 credit available
        Assert.Contains("800.00 credit available", vm.ValidationMessage);

        vm.AmountReceivedText = "400";
        Assert.Equal(800m, vm.OnAccount);
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public void Discount_IsValidatedAgainstSubtotal()
    {
        var vm = Create();
        vm.AddProductCommand.Execute(Water);

        vm.DiscountText = "50";
        Assert.Equal(250m, vm.Total);

        vm.DiscountText = "301";
        Assert.Contains("Discount cannot be more", vm.ValidationMessage);

        vm.DiscountText = "1.005";
        Assert.Contains("two decimal places", vm.ValidationMessage);
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool Confirm(string message, string title = "") => true;
        public void ShowError(string message, string title = "") { }
        public void ShowInfo(string message, string title = "") { }
        public string? Prompt(string message, string title, int maxLength) => null;
    }

    /// <summary>The calculations under test never touch the services.</summary>
    private sealed class Unused : IProductService, ICustomerService, ISalesService
    {
        private static Exception Fail() => new InvalidOperationException("Service should not be called.");

        Task<PagedResult<ProductListItem>> IProductService.SearchAsync(ProductQuery query, CancellationToken ct) => throw Fail();
        Task<ProductListItem> IProductService.GetAsync(Guid id, CancellationToken ct) => throw Fail();
        Task<IReadOnlyList<ProductListItem>> IProductService.FindForSaleAsync(string text, int maxResults, CancellationToken ct) => throw Fail();
        Task<Guid> IProductService.CreateAsync(CreateProductRequest request, CancellationToken ct) => throw Fail();
        Task IProductService.UpdateAsync(UpdateProductRequest request, CancellationToken ct) => throw Fail();
        Task IProductService.SetActiveAsync(Guid id, bool isActive, CancellationToken ct) => throw Fail();
        Task IProductService.ReceiveStockAsync(Guid productId, int quantity, string reason, CancellationToken ct) => throw Fail();
        Task IProductService.AdjustStockAsync(Guid productId, int quantityChange, string reason, CancellationToken ct) => throw Fail();
        Task<PagedResult<StockMovementItem>> IProductService.GetMovementsAsync(Guid productId, int page, int pageSize, CancellationToken ct) => throw Fail();
        Task<PagedResult<CustomerListItem>> ICustomerService.SearchAsync(CustomerQuery query, CancellationToken ct) => throw Fail();
        Task<CustomerListItem> ICustomerService.GetAsync(Guid id, CancellationToken ct) => throw Fail();
        Task<Guid> ICustomerService.CreateAsync(CustomerDetailsRequest request, CancellationToken ct) => throw Fail();
        Task ICustomerService.UpdateAsync(Guid id, CustomerDetailsRequest request, CancellationToken ct) => throw Fail();
        Task ICustomerService.SetActiveAsync(Guid id, bool isActive, CancellationToken ct) => throw Fail();
        Task ICustomerService.RecordPaymentAsync(Guid customerId, decimal amount, PaymentMethod method, string? note, CancellationToken ct) => throw Fail();
        Task<PagedResult<CustomerTransactionItem>> ICustomerService.GetTransactionsAsync(Guid customerId, int page, int pageSize, CancellationToken ct) => throw Fail();
        Task<SaleDetails> ISalesService.CreateAsync(CreateSaleRequest request, CancellationToken ct) => throw Fail();
        Task ISalesService.VoidAsync(Guid saleId, string reason, CancellationToken ct) => throw Fail();
        Task<SaleDetails> ISalesService.GetAsync(Guid saleId, CancellationToken ct) => throw Fail();
        Task<PagedResult<SaleListItem>> ISalesService.SearchAsync(SaleQuery query, CancellationToken ct) => throw Fail();
    }
}
