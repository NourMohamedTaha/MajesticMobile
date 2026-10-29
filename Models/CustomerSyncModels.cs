using System;
using System.Collections.Generic;

namespace MajesticMobile.Models
{
    public class CustomerRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public int LocationId { get; set; }
    }

    public class PayDebtRequest
    {
        public int CustomerId { get; set; }
        public decimal Amount { get; set; }
        public int LocationId { get; set; }
    }

    public class AddCustomerResponse
    {
        public int Id { get; set; }
    }

    public class LedgerEntryModel
    {
        public int RefId { get; set; }
        public DateTime TransDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string TransType { get; set; } = string.Empty;
        public decimal RunningBalance { get; set; }
    }

    public class CustomerLedgerResponse
    {
        public decimal InitialBalance { get; set; }
        public List<LedgerEntryModel> Entries { get; set; } = new();
    }
}