/**
 * Mock invoice data for the E0-05 "design only this pass" build
 * (docs/SCREEN_DESIGNS.md § E0-05, ACTION_PLAN.md §12.3).
 *
 * THERE IS NO INVOICING BACKEND. No `InvoicesController`, no invoice entity,
 * no migration — epic E8 has not started, and E8-08 (the invoice numbering
 * format) is separately blocked on FSD Q9c (numbering format, GSTIN,
 * registered address, bank details). Everything in this file is a
 * hand-written, realistic-looking sample so the business owner has a
 * concrete screen to sign off — none of it is fetched over HTTP, and none of
 * it should ever be mistaken for real data. Both invoice screens render a
 * `.banner-warning` saying exactly that.
 *
 * What is genuinely real: invoice statuses and service types. Those are
 * configurable master data served by the live `GET /api/v1/master-data`
 * aggregate (`invoiceStatuses` / `serviceTypes`) and MUST be resolved through
 * `MasterDataService` by the components that consume this file — never
 * hard-coded. This file only supplies the code values (`DRAFT`/`ISSUED`/
 * `PAID`/`CANCELLED`, `CIF`/`FREIGHT_ONLY`) that get matched against the real
 * master-data rows; it does not define labels or colours for them.
 *
 * The freight-only value was `'Freight-only'` until the M5 screen pass — that is
 * the seeded *label*, not the code the API serialises. See
 * `shared/constants/service-type-codes.ts`.
 */

export type MockServiceTypeCode = 'CIF' | 'FREIGHT_ONLY';
export type MockInvoiceStatusCode = 'DRAFT' | 'ISSUED' | 'PAID' | 'CANCELLED';

export interface MockInvoiceCustomer {
  id: string;
  businessName: string;
  contactName: string;
  phone: string;
  address: string;
}

export interface MockInvoiceLine {
  description: string;
  amount: number;
}

export interface MockInvoice {
  id: string;
  /** Visible placeholder only — the real numbering format is Q9c-blocked (E8-08). */
  invoiceNumber: string;
  customerId: string;
  serviceTypeCode: MockServiceTypeCode;
  statusCode: MockInvoiceStatusCode;
  /** ISO date (yyyy-MM-dd). */
  issueDate: string;
  lines: MockInvoiceLine[];
  taxRatePct: number;
  /** Only present once mock-marked PAID. */
  paidDate?: string;
  paidReference?: string;
}

function round2(n: number): number {
  return Math.round(n * 100) / 100;
}

export function subtotalOf(invoice: MockInvoice): number {
  return invoice.lines.reduce((sum, line) => sum + line.amount, 0);
}

export function taxOf(invoice: MockInvoice): number {
  return round2(subtotalOf(invoice) * (invoice.taxRatePct / 100));
}

export function totalOf(invoice: MockInvoice): number {
  return round2(subtotalOf(invoice) + taxOf(invoice));
}

/** Sample bill-to directory — realistic Indian customer/business names, not real customer records. */
export const MOCK_INVOICE_CUSTOMERS: MockInvoiceCustomer[] = [
  {
    id: 'cust-meena-traders',
    businessName: 'Meena Traders',
    contactName: 'Meena Shah',
    phone: '+91 98250 41122',
    address: '14 Ring Road, Surat, Gujarat 395002'
  },
  {
    id: 'cust-shah-overseas',
    businessName: 'Shah Overseas Exports',
    contactName: 'Rakesh Shah',
    phone: '+91 98200 55210',
    address: 'B-12 Sector 5, Noida, Uttar Pradesh 201301'
  },
  {
    id: 'cust-patel-fashion',
    businessName: 'Patel Fashion House',
    contactName: 'Devika Patel',
    phone: '+91 90040 11223',
    address: '22 MG Road, Ahmedabad, Gujarat 380009'
  },
  {
    id: 'cust-global-trims',
    businessName: 'Global Trims & Accessories',
    contactName: 'Anil Kumar',
    phone: '+91 98110 33221',
    address: 'Plot 7, Okhla Industrial Area, New Delhi 110020'
  },
  {
    id: 'cust-om-sai',
    businessName: 'Om Sai Textiles',
    contactName: 'Suresh Iyer',
    phone: '+91 90360 77890',
    address: '45 Textile Market, Coimbatore, Tamil Nadu 641001'
  },
  {
    id: 'cust-aarohi-jewels',
    businessName: 'Aarohi Jewels Pvt Ltd',
    contactName: 'Kavita Nair',
    phone: '+91 98450 66112',
    address: '3rd Floor, Zaveri Bazaar, Mumbai, Maharashtra 400002'
  },
  {
    id: 'cust-zenith-leather',
    businessName: 'Zenith Leather Goods',
    contactName: 'Farhan Sheikh',
    phone: '+91 98330 44556',
    address: '18 Leather Complex, Kanpur, Uttar Pradesh 208001'
  },
  {
    id: 'cust-krishna-handicrafts',
    businessName: 'Krishna Handicrafts Co',
    contactName: 'Ramesh Yadav',
    phone: '+91 94140 22110',
    address: 'Handicraft Park, Jaipur, Rajasthan 302001'
  }
];

/** Sample invoices — a deliberate mix of both service types and all four statuses. */
export const MOCK_INVOICES: MockInvoice[] = [
  {
    id: 'inv-0001',
    invoiceNumber: 'INV-PREVIEW-0001',
    customerId: 'cust-meena-traders',
    serviceTypeCode: 'CIF',
    statusCode: 'ISSUED',
    issueDate: '2026-07-10',
    lines: [
      { description: 'Sourcing & QC — Jewellery consignment (July batch)', amount: 185000 },
      { description: 'Freight & insurance (CIF, sea)', amount: 42000 }
    ],
    taxRatePct: 18
  },
  {
    id: 'inv-0002',
    invoiceNumber: 'INV-PREVIEW-0002',
    customerId: 'cust-shah-overseas',
    serviceTypeCode: 'FREIGHT_ONLY',
    statusCode: 'DRAFT',
    issueDate: '2026-07-22',
    lines: [{ description: 'Freight forwarding — Handbags shipment', amount: 68000 }],
    taxRatePct: 18
  },
  {
    id: 'inv-0003',
    invoiceNumber: 'INV-PREVIEW-0003',
    customerId: 'cust-patel-fashion',
    serviceTypeCode: 'CIF',
    statusCode: 'PAID',
    issueDate: '2026-06-18',
    paidDate: '2026-06-25',
    paidReference: 'UTR3456781',
    lines: [
      { description: 'Sourcing fee — Footwear Q2 order', amount: 124000 },
      { description: 'Freight & insurance (CIF, sea)', amount: 31000 }
    ],
    taxRatePct: 18
  },
  {
    id: 'inv-0004',
    invoiceNumber: 'INV-PREVIEW-0004',
    customerId: 'cust-global-trims',
    serviceTypeCode: 'FREIGHT_ONLY',
    statusCode: 'CANCELLED',
    issueDate: '2026-05-30',
    lines: [{ description: 'Freight forwarding — cancelled order', amount: 52000 }],
    taxRatePct: 18
  },
  {
    id: 'inv-0005',
    invoiceNumber: 'INV-PREVIEW-0005',
    customerId: 'cust-om-sai',
    serviceTypeCode: 'CIF',
    statusCode: 'ISSUED',
    issueDate: '2026-07-15',
    lines: [
      { description: 'Sourcing & QC — Home textiles Aug batch', amount: 96000 },
      { description: 'Freight & insurance (CIF, air)', amount: 27000 }
    ],
    taxRatePct: 18
  },
  {
    id: 'inv-0006',
    invoiceNumber: 'INV-PREVIEW-0006',
    customerId: 'cust-aarohi-jewels',
    serviceTypeCode: 'CIF',
    statusCode: 'PAID',
    issueDate: '2026-06-02',
    paidDate: '2026-06-10',
    paidReference: 'NEFT88213',
    lines: [
      { description: 'Sourcing fee — Silver jewellery order', amount: 210000 },
      { description: 'Freight & insurance (CIF, sea)', amount: 48000 }
    ],
    taxRatePct: 18
  },
  {
    id: 'inv-0007',
    invoiceNumber: 'INV-PREVIEW-0007',
    customerId: 'cust-zenith-leather',
    serviceTypeCode: 'FREIGHT_ONLY',
    statusCode: 'DRAFT',
    issueDate: '2026-07-24',
    lines: [{ description: 'Freight forwarding — Bags & wallets shipment', amount: 39000 }],
    taxRatePct: 18
  },
  {
    id: 'inv-0008',
    invoiceNumber: 'INV-PREVIEW-0008',
    customerId: 'cust-krishna-handicrafts',
    serviceTypeCode: 'FREIGHT_ONLY',
    statusCode: 'ISSUED',
    issueDate: '2026-07-05',
    lines: [{ description: 'Freight forwarding — Handicrafts export batch', amount: 58500 }],
    taxRatePct: 18
  }
];
