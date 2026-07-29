namespace SourcingOps.Application.Shipments;

/// <summary>Application service behind the E7-09 shipment reference-document endpoints.</summary>
public interface IShipmentDocumentService
{
    /// <summary>Null when the shipment does not exist.</summary>
    Task<ShipmentDocumentListResultDto?> ListAsync(Guid shipmentId, CancellationToken ct = default);

    /// <summary>Null when the shipment does not exist; throws <c>AppValidationException</c> on an unknown/mis-scoped document type or a failed upload validation.</summary>
    Task<ShipmentDocumentDto?> UploadDocumentAsync(
        Guid shipmentId, Stream content, string originalFilename, string? contentType, long sizeBytes,
        Guid documentTypeId, Guid actorUserId, CancellationToken ct = default);

    Task<ShipmentDocumentDownload?> DownloadDocumentAsync(Guid documentId, CancellationToken ct = default);

    Task<bool> DeleteDocumentAsync(Guid documentId, Guid actorUserId, CancellationToken ct = default);
}
