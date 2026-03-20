namespace Workit.Shared.Payday;

public sealed class PaydayExpensesResponse
{
    public List<PaydayExpense> Expenses { get; set; } = [];
    public int PerPage { get; set; }
    public int Total   { get; set; }
    public int Page    { get; set; }
    public int Pages   { get; set; }
}
