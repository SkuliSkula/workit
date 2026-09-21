using System.Text.Json;
using FluentAssertions;
using Workit.Shared.Payday;

namespace Workit.Tests.Api;

/// <summary>
/// Payday drafts can carry lines with no quantity or VAT yet; the console must
/// still list the invoice instead of failing the whole page on one null.
/// </summary>
public class PaydayInvoiceLineJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DraftLine_WithNullQuantityAndVat_Deserializes()
    {
        const string json = """
            {"invoices":[{"id":"228dc013-9df4-4c72-be61-82f01ffeedf5","customer":null,"description":"","number":null,"status":"DRAFT",
              "created":"2026-09-15T15:44:46Z","invoiceDate":"2026-09-15T00:00:00Z","dueDate":"2026-09-30T00:00:00Z",
              "lines":[
                {"id":"11111111-1111-1111-1111-111111111111","invoiceId":"228dc013-9df4-4c72-be61-82f01ffeedf5","position":1,"description":"Vinna","quantity":2.5,"unitPriceExcludingVat":12000,"vatPercentage":24,"discountPercentage":null,"created":"2026-09-15T15:44:46Z"},
                {"id":"22222222-2222-2222-2222-222222222222","invoiceId":"228dc013-9df4-4c72-be61-82f01ffeedf5","position":2,"description":"","quantity":null,"unitPriceExcludingVat":null,"vatPercentage":null,"discountPercentage":null,"created":"2026-09-15T15:44:46Z"}
              ]}],"perPage":25,"total":1,"page":1,"pages":1}
            """;

        var response = JsonSerializer.Deserialize<PaydayInvoicesResponse>(json, Web);

        response.Should().NotBeNull();
        var lines = response!.Invoices.Single().Lines!;
        lines[0].Quantity.Should().Be(2.5m);
        lines[1].Quantity.Should().BeNull();
        lines[1].VatPercentage.Should().BeNull();
    }
}
