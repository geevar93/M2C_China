namespace SourcingOps.Domain.Entities;

/// <summary>A stocked item (FR-INV-01…09).</summary>
public class InventoryItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public Guid? VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    public string Unit { get; set; } = "pcs";
    public decimal OnHandQty { get; set; }
    public decimal ReorderThreshold { get; set; }

    public ICollection<ShipmentLine> ShipmentLines { get; set; } = new List<ShipmentLine>();
}
