namespace SourcingOps.Domain.Common;

/// <summary>Shape shared by every Code/Label/SortOrder configurable-master-data lookup table (FSD §3.3).</summary>
public interface ILookupEntity
{
    Guid Id { get; set; }
    string Code { get; set; }
    string Label { get; set; }
    bool IsActive { get; set; }
    int SortOrder { get; set; }
}
