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

    /// <summary>External purchase reference for freight-only customers (FSD Q1, provisional).</summary>
    public string? ExternalPurchaseReference { get; set; }

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
