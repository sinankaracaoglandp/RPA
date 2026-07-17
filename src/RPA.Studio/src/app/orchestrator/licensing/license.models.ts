/** WebAPI `GET /api/license/status` yaniti (RPA.Domain.Licensing.LicenseStatus karsiligi). */
export interface LicenseStatus {
  isInstalled: boolean;
  isValid: boolean;
  licenseId: string | null;
  revision: number | null;
  customerId: string | null;
  customerName: string | null;
  edition: string | null;
  expiresAt: string | null;
  maxActivatedAgents: number;
  activatedAgents: number;
  features: string[];
  errorCode: string | null;
}

/** WebAPI `GET /api/license/installation-request` yaniti. Gizli anahtar icermez (yalniz public). */
export interface InstallationRequestDocument {
  schemaVersion: number;
  installationId: string;
  installationPublicKey: string;
  installationPublicKeyFingerprint: string;
  productId: string;
  customerReference: string | null;
  createdAt: string;
}

/** `POST /api/license/import` govdesi — saticidan gelen imzali .lic belgesi (PascalCase alanlar). */
export interface SignedLicenseDocument {
  Payload: unknown;
  Signature: string;
  Algorithm?: string;
}
