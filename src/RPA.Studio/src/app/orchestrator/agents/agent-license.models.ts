/**
 * Agent kimlik durumlari (RPA.Domain.Enums.AgentIdentityStatus karsiligi).
 * KOLTUK SEMANTIGI (kontrat): yalnizca `Activated` ve `Disabled` lisans koltugu tuketir;
 * `PendingActivation` ve `Deactivated` tuketmez. Bu bilgi UI'da yalnizca ETIKET amaclidir —
 * kullanilan koltuk sayisi HER ZAMAN WebAPI'den (GET /api/license/status) okunur.
 */
export type AgentIdentityStatus = 'PendingActivation' | 'Activated' | 'Disabled' | 'Deactivated';

/** WebAPI `GET /api/agents` ogesi. Credential/aktivasyon hash'i icermez. */
export interface AgentDto {
  id: string;
  name: string;
  status: AgentIdentityStatus;
  machineFingerprint: string | null;
  lastSeenAt: string | null;
}

/**
 * `POST /api/agents/{id}/activation-code` yaniti. `activationCode` plaintext'tir ve
 * YALNIZCA bir kez gosterilir: bellekte tutulur, hicbir depolamaya yazilmaz.
 */
export interface ActivationCodeResponse {
  agentId: string;
  activationCode: string;
  expiresAt: string;
}

/**
 * `POST /api/agents/{id}/rotate-credential` yaniti. `credential` plaintext'tir ve YALNIZCA bir kez
 * gosterilir: bellekte tutulur, hicbir depolamaya yazilmaz. Rotasyon eski credential'i DERHAL
 * gecersiz kilar (WebAPI yalnizca yeni hash'i saklar).
 */
export interface RotateCredentialResponse {
  agentId: string;
  credential: string;
}

/** Bir durumun lisans koltugu tuketip tuketmedigi (yalnizca gosterim etiketi). */
export function consumesSeat(status: AgentIdentityStatus): boolean {
  return status === 'Activated' || status === 'Disabled';
}
