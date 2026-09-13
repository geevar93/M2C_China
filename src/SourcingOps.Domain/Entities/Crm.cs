namespace SourcingOps.Domain.Entities;

/// <summary>A lead/customer record (FR-CRM-01…11).</summary>
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }

    /// <summary>Normalised international format (e.g. +91XXXXXXXXXX) — FR-CRM-04.</summary>
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>
    /// The BUYER's GST identification number (N-37), distinct from the seller's own GSTIN on
    /// <c>CompanySettings</c>. Deliberately nullable/optional — required-on-save would block
    /// creating customers who are not GST-registered (individuals, small retailers) and would
    /// strand every existing row. Stored trimmed and uppercased; see
    /// <c>CustomerService</c>'s normalisation/validation for the exact rule.
    /// </summary>
    public string? Gstin { get; set; }

    /// <summary>
    /// Two-digit GST state code (see <c>IndianStateCodes</c>) — the buyer's PLACE OF SUPPLY.
    /// Comparing it with the seller's code decides CGST+SGST (same state) versus IGST
    /// (different states), so it is the single input the tax split turns on.
    ///
    /// Nullable for the same reason <see cref="Gstin"/> is: requiring it on save would block
    /// creating non-registered customers and strand every existing row. Defaulted from the
    /// GSTIN's first two digits when one is supplied and this is blank. An invoice cannot be
    /// ISSUED while it is still null — <c>InvoiceService</c> refuses rather than guessing a
    /// tax treatment, since guessing wrong means charging the wrong tax on a legal document.
    /// </summary>
    public string? StateCode { get; set; }

    public string? City { get; set; }
    public string? Region { get; set; }
    public string? SourceChannel { get; set; }

    public Guid ServiceTypeId { get; set; }
    public ServiceType ServiceType { get; set; } = null!;

    public Guid StatusId { get; set; }
    public LeadStatus Status { get; set; } = null!;

    public Guid? OwnerUserId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Free-form tags (FR-CRM-11 [C]) — Postgres text[].</summary>
    public string[] Tags { get; set; } = [];

    // ---- External-purchase detail (freight-only customers only) — FSD Q1, answered
    // 2026-07-27 (ACTION_PLAN §10, OI-8). Structured rather than one free-text field so the
    // values are reportable. ALL SIX are nullable: only freight-only customers populate
    // any of them, and per FSD A8 staff often will not know the order value at intake
    // time. Schema only this pass — nothing in M1/M2 populates or reads these yet; the
    // customer intake/detail screens are M3 (E4-12/E4-13) scope.

    /// <summary>E.g. "Amazon", "Alibaba" — free text, not a lookup (not one of FSD §3.3's configurable categories).</summary>
    public string? ExternalMarketplace { get; set; }

    /// <summary>The marketplace's own order/reference number.</summary>
    public string? ExternalOrderRef { get; set; }

    public string? ExternalSupplierName { get; set; }

    /// <summary>Decimal, not text — the whole point of Q1's structured-over-free-text decision was to make this reportable.</summary>
    public decimal? ExternalOrderValue { get; set; }

    /// <summary>ISO 4217 code, e.g. "USD"/"CNY" — mirrors <c>invoices.currency</c>'s multi-currency future-proofing (TECH_SPEC §9, FSD A7).</summary>
    public string? ExternalOrderCurrency { get; set; }

    public DateTime? ExternalOrderDate { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<CustomerCategory> CustomerCategories { get; set; } = new List<CustomerCategory>();
    public ICollection<Interaction> Interactions { get; set; } = new List<Interaction>();
}

/// <summary>Many-to-many: category interest on a customer.</summary>
public class CustomerCategory
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

/// <summary>A note/activity entry on a customer's timeline (FR-CRM-07, FR-CRM-10).</summary>
public class Interaction
{
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid AuthorUserId { get; set; }
    public User Author { get; set; } = null!;

    /// <summary>
    /// Discriminator, e.g. "Note"/"Call"/"StatusChange". Kept as a plain string, not a
    /// lookup table — it is not one of the FSD's configurable master-data categories
    /// (categories/service types/lead-shipment-invoice statuses) and is not itemized in
    /// TECH_SPEC §6 or ACTION_PLAN E3's seeded-lookup list.
    /// </summary>
    public string Type { get; set; } = "Note";
    public string Text { get; set; } = string.Empty;
    public DateTime? FollowUpDate { get; set; }
    public DateTime CreatedAt { get; set; }
}
