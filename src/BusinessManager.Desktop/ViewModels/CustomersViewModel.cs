using System.Collections.ObjectModel;
using BusinessManager.Application.Customers;
using BusinessManager.Desktop.Services;
using BusinessManager.Desktop.Support;
using BusinessManager.Domain.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop.ViewModels;

public sealed partial class CustomersViewModel : ViewModelBase, IDisposable
{
    private const int PageSize = 50;
    private const int LedgerSize = 100;

    private readonly ICustomerService _customers;
    private readonly IDialogService _dialogs;
    private readonly Debouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
    private readonly LatestOnly _listLoads = new();
    private readonly LatestOnly _detailLoads = new();

    public CustomersViewModel(
        ICustomerService customers,
        IDialogService dialogs,
        ILogger<CustomersViewModel> logger)
        : base(logger)
    {
        _customers = customers;
        _dialogs = dialogs;
    }

    public ObservableCollection<CustomerListItem> Customers { get; } = new();
    public ObservableCollection<CustomerTransactionItem> Transactions { get; } = new();
    public IReadOnlyList<PaymentMethod> PaymentMethods { get; } = Enum.GetValues<PaymentMethod>();

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private bool _includeArchived;

    [ObservableProperty]
    private bool _withBalanceOnly;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _pageCount = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CustomerListItem? _selectedCustomer;

    public bool HasSelection => SelectedCustomer is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool _isNew = true;

    public string EditorTitle => IsNew ? "New customer" : "Edit customer";

    [ObservableProperty]
    private string? _editName;

    [ObservableProperty]
    private string? _editPhone;

    [ObservableProperty]
    private string? _editEmail;

    [ObservableProperty]
    private string? _editNotes;

    [ObservableProperty]
    private string? _editCreditLimit;

    [ObservableProperty]
    private string? _paymentAmount;

    [ObservableProperty]
    private PaymentMethod _paymentMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private string? _paymentNote;

    public override Task ActivateAsync() => LoadAsync(keepSelection: true);

    partial void OnSearchTextChanged(string? value) =>
        _searchDebouncer.Run(_ => LoadAsync(resetPage: true));

    partial void OnIncludeArchivedChanged(bool value) => _ = LoadAsync(resetPage: true);

    partial void OnWithBalanceOnlyChanged(bool value) => _ = LoadAsync(resetPage: true);

    partial void OnSelectedCustomerChanged(CustomerListItem? value)
    {
        if (value is null)
        {
            return;
        }

        IsNew = false;
        EditName = value.Name;
        EditPhone = value.Phone;
        EditEmail = value.Email;
        EditNotes = value.Notes;
        EditCreditLimit = InputParser.FormatMoney(value.CreditLimit);
        PaymentAmount = null;
        PaymentNote = null;
        _ = LoadTransactionsAsync(value.Id);
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(keepSelection: true);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousPageAsync()
    {
        Page--;
        await LoadAsync();
    }

    private bool CanGoPrevious() => Page > 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync()
    {
        Page++;
        await LoadAsync();
    }

    private bool CanGoNext() => Page < PageCount;

    [RelayCommand]
    private void NewCustomer()
    {
        SelectedCustomer = null;
        Transactions.Clear();
        IsNew = true;
        EditName = EditPhone = EditEmail = EditNotes = null;
        EditCreditLimit = InputParser.FormatMoney(0m);
        ErrorMessage = StatusMessage = null;
    }

    [RelayCommand]
    private async Task SaveCustomerAsync()
    {
        if (!InputParser.TryParseMoney(EditCreditLimit, "Credit limit", out decimal limit, out string? error, blankIsZero: true))
        {
            ShowError(error!);
            return;
        }

        var request = new CustomerDetailsRequest(EditName ?? "", EditPhone, EditEmail, EditNotes, limit);

        if (IsNew)
        {
            var (ok, id) = await RunAsync(ct => _customers.CreateAsync(request, ct));
            if (ok)
            {
                ShowStatus($"Customer {request.Name.Trim()} added.");
                await LoadAsync(selectId: id);
            }
        }
        else if (SelectedCustomer is { } selected)
        {
            if (limit < selected.Balance && !_dialogs.Confirm(
                    $"{selected.Name} owes {selected.Balance:N2}, which is more than the new limit. " +
                    "No further credit will be allowed until the balance is reduced. Continue?"))
            {
                return;
            }

            if (await RunAsync(ct => _customers.UpdateAsync(selected.Id, request, ct)))
            {
                ShowStatus("Customer saved.");
                await LoadAsync(selectId: selected.Id);
            }
        }
    }

    [RelayCommand]
    private async Task ToggleArchiveAsync()
    {
        if (SelectedCustomer is not { } selected)
        {
            return;
        }

        bool archive = selected.IsActive;

        if (archive && !_dialogs.Confirm($"Archive {selected.Name}? Their history is kept and they can be restored later."))
        {
            return;
        }

        if (await RunAsync(ct => _customers.SetActiveAsync(selected.Id, !archive, ct)))
        {
            ShowStatus(archive ? $"{selected.Name} archived." : $"{selected.Name} restored.");
            await LoadAsync(selectId: selected.Id);
        }
    }

    [RelayCommand]
    private async Task RecordPaymentAsync()
    {
        if (SelectedCustomer is not { } selected)
        {
            return;
        }

        if (!InputParser.TryParseMoney(PaymentAmount, "Payment amount", out decimal amount, out string? error))
        {
            ShowError(error!);
            return;
        }

        var method = PaymentMethod;
        string? note = PaymentNote;

        if (await RunAsync(ct => _customers.RecordPaymentAsync(selected.Id, amount, method, note, ct)))
        {
            ShowStatus($"Payment of {amount:N2} recorded for {selected.Name}.");
            await LoadAsync(selectId: selected.Id);
        }
    }

    [RelayCommand]
    private void PayFullBalance()
    {
        if (SelectedCustomer is { Balance: > 0 } selected)
        {
            PaymentAmount = InputParser.FormatMoney(selected.Balance);
        }
    }

    private async Task LoadAsync(bool resetPage = false, bool keepSelection = false, Guid? selectId = null)
    {
        if (resetPage)
        {
            Page = 1;
        }

        Guid? wanted = selectId ?? (keepSelection ? SelectedCustomer?.Id : null);
        var query = new CustomerQuery(SearchText, IncludeArchived, WithBalanceOnly, Page, PageSize);
        var token = _listLoads.Next();

        var (ok, result) = await RunAsync(ct => _customers.SearchAsync(query, ct), token, clearMessages: false);
        if (!ok || result is null)
        {
            return;
        }

        if (result.Items.Count == 0 && result.TotalCount > 0 && Page > 1)
        {
            Page = result.PageCount;
            await LoadAsync(selectId: wanted);
            return;
        }

        Customers.Clear();
        foreach (var item in result.Items)
        {
            Customers.Add(item);
        }

        TotalCount = result.TotalCount;
        PageCount = result.PageCount;

        if (wanted is { } id)
        {
            var match = Customers.FirstOrDefault(c => c.Id == id);
            if (match is null)
            {
                var (found, customer) = await RunAsync(ct => _customers.GetAsync(id, ct), clearMessages: false);
                match = found ? customer : null;
            }

            if (match is null)
            {
                NewCustomer();
            }
            else if (Equals(SelectedCustomer, match))
            {
                OnSelectedCustomerChanged(match);
            }
            else
            {
                SelectedCustomer = match;
            }
        }
    }

    private async Task LoadTransactionsAsync(Guid customerId)
    {
        var token = _detailLoads.Next();
        var (ok, ledger) = await RunAsync(
            ct => _customers.GetTransactionsAsync(customerId, 1, LedgerSize, ct),
            token,
            clearMessages: false);

        if (!ok || ledger is null)
        {
            return;
        }

        Transactions.Clear();
        foreach (var entry in ledger.Items)
        {
            Transactions.Add(entry);
        }
    }

    public void Dispose() => _searchDebouncer.Dispose();
}
