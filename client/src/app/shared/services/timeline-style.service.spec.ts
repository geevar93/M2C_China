import { TestBed } from '@angular/core/testing';
import { TimelineStyleService } from './timeline-style.service';

describe('TimelineStyleService', () => {
  let service: TimelineStyleService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(TimelineStyleService);
  });

  it('maps every kind that has a direct baseTimeline precedent to the prototype\'s exact hex', () => {
    expect(service.dotColor('EnquiryCaptured')).toBe('#6b7280');
    expect(service.dotColor('NoteAdded')).toBe('#6b7280');
    expect(service.dotColor('StatusChanged')).toBe('#2e7d32');
    expect(service.dotColor('CatalogDispatched')).toBe('#2d5be3');
    expect(service.dotColor('ShipmentRecorded')).toBe('#f57f17');
  });

  it('assigns a colour to the kinds with no prototype precedent instead of leaving them blank', () => {
    expect(service.dotColor('OwnerChanged')).toBe('#1565c0');
    expect(service.dotColor('InvoiceCreated')).toBe('#1565c0');
    expect(service.dotColor('InvoiceStatusChanged')).toBe('#2e7d32');
  });

  it('gives InvoiceDispatched the same dispatch blue as CatalogDispatched (D-67)', () => {
    // The two kinds are deliberately DISTINCT but share a colour: both are "sent via
    // WhatsApp", and DESIGN_TOKENS has no precedent for a separate invoice-send colour.
    // Pinned so nobody "fixes" the apparent duplication by inventing a new hex, and so the
    // kind surviving as a separate value is visible here rather than only in the model union.
    expect(service.dotColor('InvoiceDispatched')).toBe('#2d5be3');
    expect(service.dotColor('InvoiceDispatched')).toBe(service.dotColor('CatalogDispatched'));
  });

  it('falls back to the muted grey for an unknown or missing kind', () => {
    expect(service.dotColor('SomethingNew')).toBe('#6b7280');
    expect(service.dotColor(null)).toBe('#6b7280');
    expect(service.dotColor(undefined)).toBe('#6b7280');
  });
});
